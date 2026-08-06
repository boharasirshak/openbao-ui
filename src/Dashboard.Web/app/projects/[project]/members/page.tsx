"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Button,
  Checkbox,
  Chip,
  FormControlLabel,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import {
  AccessDenied,
  EmptyState,
  LoadingRow,
  PageHeader,
  useSession,
} from "@/components/AppShell";
import FormDialog from "@/components/FormDialog";
import {
  errorMessage,
  isForbidden,
  listProjectMembers,
  projectMemberOptions,
  setProjectRoles,
  type ProjectMember,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";

/**
 * Who can touch this project, managed from inside the project. One dialog: pick a
 * person, tick the roles. Policy names exist underneath but never reach the screen.
 */
export default function MembersPage() {
  const project = String(useParams<{ project: string }>().project);
  const queryClient = useQueryClient();
  const session = useSession();
  const [error, setError] = useState("");
  const [editing, setEditing] = useState<ProjectMember | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<ProjectMember | null>(null);

  const members = useQuery({
    queryKey: keys.projectMembers(project),
    queryFn: () => listProjectMembers(project),
    retry: false,
  });
  const options = useQuery({
    queryKey: [...keys.projectMembers(project), "options"],
    queryFn: () => projectMemberOptions(project),
    retry: false,
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.projectMembers(project) });

  const save = useMutation({
    mutationFn: (input: { username: string; policies: string[] }) =>
      setProjectRoles(project, input.username, input.policies),
    onSuccess: async () => {
      setError("");
      await refresh();
    },
    onError: (failure) => setError(errorMessage(failure, "The change did not go through.")),
  });

  if (members.isError && isForbidden(members.error)) {
    return <AccessDenied what="project members" />;
  }

  const rows = members.data ?? [];
  const existing = new Set(rows.map((member) => member.username));
  // People not yet on the project, for the add dialog.
  const candidates = (options.data?.users ?? []).filter((user) => !existing.has(user));

  return (
    <>
      <PageHeader
        title="Members"
        description="Who can see or change this project's secrets, and as what."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setAdding(true)}>
            Add member
          </Button>
        }
      />

      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError("")}>
          {error}
        </Alert>
      )}
      {members.isError && !isForbidden(members.error) && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(members.error, "The member list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {members.isLoading ? (
          <LoadingRow label="Loading members…" />
        ) : rows.length === 0 ? (
          <EmptyState
            title="Nobody has access yet"
            hint="Add a member and pick what they can do."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell width={220}>Person</TableCell>
                <TableCell>Access</TableCell>
                <TableCell width={200} align="right" />
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((member) => (
                <TableRow key={member.username} hover>
                  <TableCell>
                    {member.username}
                    {member.username === session?.username && (
                      <Typography variant="caption" display="block" color="text.secondary">
                        you
                      </Typography>
                    )}
                    {member.disabled && (
                      <Chip size="small" color="warning" variant="outlined" label="disabled" />
                    )}
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {member.roles.map((role) => (
                        <Chip
                          key={role.policy}
                          size="small"
                          variant="outlined"
                          label={role.label}
                        />
                      ))}
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <Button size="small" onClick={() => setEditing(member)}>
                      Change access
                    </Button>
                    <Button size="small" color="error" onClick={() => setRemoving(member)}>
                      Remove
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <RolesDialog
        open={adding}
        title="Add member"
        submitLabel="Add to project"
        users={candidates}
        roles={options.data?.roles ?? []}
        onClose={() => setAdding(false)}
        onSubmit={(username, policies) => save.mutateAsync({ username, policies })}
      />

      <RolesDialog
        open={editing !== null}
        title={`Access for ${editing?.username ?? ""}`}
        submitLabel="Save access"
        users={editing ? [editing.username] : []}
        fixedUser={editing?.username}
        initialPolicies={editing?.roles.map((role) => role.policy) ?? []}
        roles={options.data?.roles ?? []}
        onClose={() => setEditing(null)}
        onSubmit={(username, policies) => save.mutateAsync({ username, policies })}
      />

      <FormDialog
        open={removing !== null}
        title={`Remove ${removing?.username ?? ""}?`}
        submitLabel="Remove from project"
        onClose={() => setRemoving(null)}
        onSubmit={async () => {
          if (removing) {
            await save.mutateAsync({ username: removing.username, policies: [] });
          }
        }}
      >
        <Alert severity="warning">
          They lose all access to {project}. Their account and their access to other projects stay
          as they are.
        </Alert>
      </FormDialog>
    </>
  );
}

/** One dialog for both add and edit: pick a person (unless fixed), tick roles. */
function RolesDialog({
  open,
  title,
  submitLabel,
  users,
  fixedUser,
  initialPolicies = [],
  roles,
  onClose,
  onSubmit,
}: {
  open: boolean;
  title: string;
  submitLabel: string;
  users: string[];
  fixedUser?: string;
  initialPolicies?: string[];
  roles: { policy: string; label: string }[];
  onClose: () => void;
  onSubmit: (username: string, policies: string[]) => Promise<unknown>;
}) {
  const [username, setUsername] = useState<string | null>(null);
  const [picked, setPicked] = useState<Set<string> | null>(null);

  // Lazy-init from props so reopening the dialog for another member starts fresh.
  const effectiveUser = fixedUser ?? username;
  const selected = picked ?? new Set(initialPolicies);

  function reset() {
    setUsername(null);
    setPicked(null);
    onClose();
  }

  return (
    <FormDialog
      open={open}
      title={title}
      submitLabel={submitLabel}
      disabled={!effectiveUser || selected.size === 0}
      onClose={reset}
      onSubmit={async () => {
        if (effectiveUser) {
          await onSubmit(effectiveUser, [...selected]);
        }
      }}
    >
      {!fixedUser && (
        <Autocomplete
          options={users}
          value={username}
          onChange={(_event, value) => setUsername(value)}
          renderInput={(params) => <TextField {...params} label="Person" autoFocus />}
        />
      )}
      <Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
          What they can do
        </Typography>
        {roles.map((role) => (
          <FormControlLabel
            key={role.policy}
            control={
              <Checkbox
                checked={selected.has(role.policy)}
                onChange={(event) => {
                  const next = new Set(selected);
                  if (event.target.checked) {
                    next.add(role.policy);
                  } else {
                    next.delete(role.policy);
                  }
                  setPicked(next);
                }}
              />
            }
            label={role.label}
          />
        ))}
      </Stack>
    </FormDialog>
  );
}
