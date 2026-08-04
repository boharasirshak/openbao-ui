"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Button,
  Chip,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/GroupAddOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import PeopleIcon from "@mui/icons-material/PeopleAltOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import FormDialog from "@/components/FormDialog";
import { mono } from "@/lib/theme";
import {
  createTeam,
  deleteTeam,
  errorMessage,
  listAssignablePolicies,
  listMembers,
  listTeams,
  setTeamMembers,
  setTeamRoles,
  type TeamSummary,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";

export default function TeamsPage() {
  const queryClient = useQueryClient();
  const teams = useQuery({ queryKey: keys.teams, queryFn: listTeams });
  const members = useQuery({ queryKey: keys.members, queryFn: listMembers });
  const policies = useQuery({
    queryKey: keys.assignablePolicies,
    queryFn: listAssignablePolicies,
  });

  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [newRoles, setNewRoles] = useState<string[]>([]);
  const [editingRoles, setEditingRoles] = useState<TeamSummary | null>(null);
  const [rolesDraft, setRolesDraft] = useState<string[]>([]);
  const [editingMembers, setEditingMembers] = useState<TeamSummary | null>(null);
  const [membersDraft, setMembersDraft] = useState<string[]>([]);

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.teams });
  const byEntity = new Map(
    (members.data ?? []).map((member) => [member.entityId, member.username]),
  );

  async function act(action: () => Promise<unknown>) {
    setError("");
    try {
      await action();
      await refresh();
    } catch (actionError) {
      setError(errorMessage(actionError, "That did not work."));
    }
  }

  return (
    <>
      <PageHeader
        title="Teams"
        description="Grant access to a group rather than person by person. Members pick up a team's roles the next time they sign in."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New team
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}
      {teams.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(teams.error, "The team list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "hidden" }}>
        {teams.isLoading ? (
          <LoadingRow label="Loading teams…" />
        ) : (teams.data ?? []).length === 0 ? (
          <EmptyState
            title="No teams yet"
            hint="Create one per squad, then give it the roles that squad needs."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Team</TableCell>
                <TableCell>Roles</TableCell>
                <TableCell>Members</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(teams.data ?? []).map((team) => (
                <TableRow key={team.name} hover>
                  <TableCell sx={{ fontFamily: mono, fontSize: 13 }}>{team.name}</TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {team.roles.filter((role) => role !== "default").length === 0 && (
                        <Chip size="small" variant="outlined" label="no roles" />
                      )}
                      {team.roles
                        .filter((role) => role !== "default")
                        .map((role) => (
                          <Chip key={role} size="small" label={role} />
                        ))}
                    </Stack>
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {team.memberEntityIds.length === 0 && (
                        <Chip size="small" variant="outlined" label="empty" />
                      )}
                      {team.memberEntityIds.map((entityId) => (
                        <Chip
                          key={entityId}
                          size="small"
                          variant="outlined"
                          label={byEntity.get(entityId) ?? entityId.slice(0, 8)}
                        />
                      ))}
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" justifyContent="flex-end">
                      <Tooltip title="Change roles">
                        <IconButton
                          size="small"
                          onClick={() => {
                            setEditingRoles(team);
                            setRolesDraft(team.roles);
                          }}
                        >
                          <ShieldIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Change members">
                        <IconButton
                          size="small"
                          onClick={() => {
                            setEditingMembers(team);
                            setMembersDraft(team.memberEntityIds);
                          }}
                        >
                          <PeopleIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Delete team">
                        <IconButton
                          size="small"
                          color="error"
                          onClick={() => {
                            if (
                              window.confirm(
                                `Delete team "${team.name}"? Its members lose the access it granted.`,
                              )
                            ) {
                              void act(() => deleteTeam(team.name));
                            }
                          }}
                        >
                          <DeleteIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: "block" }}>
        Teams are OpenBao identity groups, so membership is enforced by OpenBao rather than by this
        application.
      </Typography>

      <FormDialog
        open={creating}
        title="New team"
        submitLabel="Create team"
        disabled={!name.trim()}
        onClose={() => {
          setCreating(false);
          setName("");
          setNewRoles([]);
        }}
        onSubmit={async () => {
          await createTeam(name.trim(), ["default", ...newRoles]);
          await refresh();
        }}
      >
        <TextField
          label="Team name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          helperText="Letters, digits, dashes and underscores."
          autoFocus
          required
        />
        <RolePicker options={policies.data ?? []} value={newRoles} onChange={setNewRoles} />
      </FormDialog>

      <FormDialog
        open={editingRoles !== null}
        title={`Roles for ${editingRoles?.name ?? ""}`}
        submitLabel="Save roles"
        onClose={() => setEditingRoles(null)}
        onSubmit={async () => {
          if (!editingRoles) return;
          await setTeamRoles(editingRoles.name, [
            "default",
            ...rolesDraft.filter((r) => r !== "default"),
          ]);
          await refresh();
        }}
      >
        <RolePicker
          options={policies.data ?? []}
          value={rolesDraft.filter((role) => role !== "default")}
          onChange={setRolesDraft}
        />
        <Alert severity="info">
          Members already signed in keep their current access until their session expires.
        </Alert>
      </FormDialog>

      <FormDialog
        open={editingMembers !== null}
        title={`Members of ${editingMembers?.name ?? ""}`}
        submitLabel="Save members"
        onClose={() => setEditingMembers(null)}
        onSubmit={async () => {
          if (!editingMembers) return;
          await setTeamMembers(editingMembers.name, membersDraft);
          await refresh();
        }}
      >
        <Autocomplete
          multiple
          options={(members.data ?? []).map((member) => member.entityId)}
          value={membersDraft}
          onChange={(_, next) => setMembersDraft(next)}
          getOptionLabel={(entityId) => byEntity.get(entityId) ?? entityId}
          renderValue={(value, getItemProps) =>
            value.map((entityId, index) => (
              <Chip
                size="small"
                label={byEntity.get(entityId) ?? entityId.slice(0, 8)}
                {...getItemProps({ index })}
                key={entityId}
              />
            ))
          }
          renderInput={(params) => (
            <TextField
              {...params}
              label="Members"
              helperText="Everyone who should inherit this team's roles."
            />
          )}
        />
      </FormDialog>
    </>
  );
}

function RolePicker({
  options,
  value,
  onChange,
}: {
  options: string[];
  value: string[];
  onChange: (next: string[]) => void;
}) {
  return (
    <Autocomplete
      multiple
      autoSelect
      options={options.filter((option) => option !== "default")}
      value={value}
      onChange={(_, next) => onChange(next)}
      renderValue={(selected, getItemProps) =>
        selected.map((role, index) => (
          <Chip size="small" label={role} {...getItemProps({ index })} key={role} />
        ))
      }
      renderInput={(params) => (
        <TextField
          {...params}
          label="Roles"
          helperText="Only policies that actually exist are offered."
        />
      )}
    />
  );
}
