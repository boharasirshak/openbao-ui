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
} from "@mui/material";
import AddIcon from "@mui/icons-material/PersonAddAltOutlined";
import BlockIcon from "@mui/icons-material/BlockOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import KeyIcon from "@mui/icons-material/VpnKeyOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import FormDialog from "@/components/FormDialog";
import { mono } from "@/lib/theme";
import { keys } from "@/lib/queryKeys";
import {
  assignMemberRoles,
  createMember,
  deleteMember,
  disableMember,
  errorMessage,
  listMembers,
  listAssignablePolicies,
  updateMember,
  type MemberResponse,
} from "@/lib/client";

const ADMIN_POLICY = "wrapper-admin";

export default function MembersPage() {
  const queryClient = useQueryClient();
  const members = useQuery({ queryKey: keys.members, queryFn: listMembers });
  const roles = useQuery({ queryKey: keys.assignablePolicies, queryFn: listAssignablePolicies });

  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [policies, setPolicies] = useState<string[]>([]);

  const [resetting, setResetting] = useState<MemberResponse | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [editingRoles, setEditingRoles] = useState<MemberResponse | null>(null);
  const [rolesDraft, setRolesDraft] = useState<string[]>([]);

  const roleOptions = [ADMIN_POLICY, ...(roles.data ?? [])];
  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.members });

  async function act(action: () => Promise<void>) {
    setError("");
    try {
      await action();
      await refresh();
    } catch (actionError) {
      setError(errorMessage(actionError, "That action failed."));
    }
  }

  return (
    <>
      <PageHeader
        title="Members"
        description="People who sign in with a username and password. Roles decide what they can read and write."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            Add member
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}
      {members.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(members.error, "The member list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "hidden" }}>
        {members.isLoading ? (
          <LoadingRow label="Loading members…" />
        ) : (members.data ?? []).length === 0 ? (
          <EmptyState title="No members yet" hint="Add the first one to grant access." />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Username</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Roles</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(members.data ?? []).map((member) => (
                <TableRow key={member.username} hover>
                  <TableCell sx={{ fontFamily: mono, fontSize: 13 }}>{member.username}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      color={member.disabled ? "default" : "success"}
                      variant={member.disabled ? "outlined" : "filled"}
                      label={member.disabled ? "disabled" : "active"}
                    />
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                      {member.policies.length === 0 && (
                        <Chip size="small" variant="outlined" label="none" />
                      )}
                      {member.policies.map((policy) => (
                        <Chip
                          key={policy}
                          size="small"
                          label={policy}
                          color={
                            policy === ADMIN_POLICY || policy === "root" ? "primary" : "default"
                          }
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
                            setEditingRoles(member);
                            setRolesDraft(member.policies);
                          }}
                        >
                          <ShieldIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Reset password">
                        <IconButton
                          size="small"
                          onClick={() => {
                            setResetting(member);
                            setNewPassword("");
                          }}
                        >
                          <KeyIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Disable sign in">
                        <span>
                          <IconButton
                            size="small"
                            disabled={member.disabled}
                            onClick={() => {
                              if (window.confirm(`Disable ${member.username}?`)) {
                                void act(() => disableMember(member.username));
                              }
                            }}
                          >
                            <BlockIcon fontSize="small" />
                          </IconButton>
                        </span>
                      </Tooltip>
                      <Tooltip title="Delete member">
                        <IconButton
                          size="small"
                          color="error"
                          onClick={() => {
                            if (
                              window.confirm(
                                `Delete ${member.username}? Their OpenBao login and entity are removed.`,
                              )
                            ) {
                              void act(() => deleteMember(member.username));
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

      <FormDialog
        open={creating}
        title="Add member"
        submitLabel="Create member"
        disabled={!username.trim() || password.length < 8}
        onClose={() => {
          setCreating(false);
          setUsername("");
          setPassword("");
          setPolicies([]);
        }}
        onSubmit={async () => {
          await createMember(username.trim(), password, policies);
          await refresh();
        }}
      >
        <TextField
          label="Username"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          autoFocus
          required
        />
        <TextField
          label="Temporary password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          type="password"
          helperText="At least 8 characters. Share it out of band and have them change it."
          required
        />
        <RolePicker options={roleOptions} value={policies} onChange={setPolicies} />
      </FormDialog>

      <FormDialog
        open={resetting !== null}
        title={`Reset password for ${resetting?.username ?? ""}`}
        submitLabel="Reset password"
        disabled={newPassword.length < 8}
        onClose={() => setResetting(null)}
        onSubmit={async () => {
          if (!resetting) return;
          // The update endpoint sets the password and the policies together, so
          // resend the roles the member already has.
          await updateMember(resetting.username, newPassword, resetting.policies);
          await refresh();
        }}
      >
        <TextField
          label="New password"
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
          type="password"
          helperText="At least 8 characters."
          autoFocus
          required
        />
        <Alert severity="info">Existing sessions stay valid until they expire.</Alert>
      </FormDialog>

      <FormDialog
        open={editingRoles !== null}
        title={`Roles for ${editingRoles?.username ?? ""}`}
        submitLabel="Save roles"
        onClose={() => setEditingRoles(null)}
        onSubmit={async () => {
          if (!editingRoles) return;
          await assignMemberRoles(editingRoles.username, rolesDraft);
          await refresh();
        }}
      >
        <RolePicker options={roleOptions} value={rolesDraft} onChange={setRolesDraft} />
        <Alert severity="warning">
          This replaces every role the member has. Removing {ADMIN_POLICY} removes their
          administrator access.
        </Alert>
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
      freeSolo
      // Without autoSelect, a typed role is dropped on Enter and the keypress
      // submits the surrounding dialog form instead.
      autoSelect
      options={options}
      value={value}
      onChange={(_, next) => onChange(next)}
      renderInput={(params) => (
        <TextField
          {...params}
          label="Roles"
          helperText="Pick existing roles, or type a policy name and press Enter."
        />
      )}
    />
  );
}
