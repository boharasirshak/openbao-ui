"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Button,
  Chip,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import { mono } from "@/lib/theme";
import { createRole, deleteRole, errorMessage, listAdminProjects, listRoles } from "@/lib/client";

const ENVIRONMENTS = ["development", "staging", "production"];

export default function RolesPage() {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: ["roles"], queryFn: listRoles });
  const projects = useQuery({ queryKey: ["projects"], queryFn: listAdminProjects });

  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [project, setProject] = useState("");
  const [environment, setEnvironment] = useState("development");
  const [readOnly, setReadOnly] = useState(true);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["roles"] });

  return (
    <>
      <PageHeader
        title="Roles"
        description="Each role grants access to one project and environment. Assign roles to members and machines."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New role
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}
      {roles.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(roles.error, "The role list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "hidden" }}>
        {roles.isLoading ? (
          <LoadingRow label="Loading roles…" />
        ) : (roles.data ?? []).length === 0 ? (
          <EmptyState title="No roles yet" hint="Create one per project and environment." />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Role</TableCell>
                <TableCell>Project</TableCell>
                <TableCell>Environment</TableCell>
                <TableCell>Access</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(roles.data ?? []).map((role) => (
                <TableRow key={role.name} hover>
                  <TableCell sx={{ fontFamily: mono, fontSize: 13 }}>{role.name}</TableCell>
                  <TableCell>{role.project}</TableCell>
                  <TableCell>
                    <EnvironmentChip environment={role.environment} />
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      variant="outlined"
                      color={role.readOnly ? "default" : "warning"}
                      label={role.readOnly ? "read only" : "read and write"}
                    />
                  </TableCell>
                  <TableCell align="right">
                    <Tooltip title="Delete role">
                      <IconButton
                        size="small"
                        color="error"
                        onClick={async () => {
                          if (
                            !window.confirm(
                              `Delete role "${role.name}"? Members holding it lose that access.`,
                            )
                          ) {
                            return;
                          }
                          setError("");
                          try {
                            await deleteRole(role.name);
                            await refresh();
                          } catch (deleteError) {
                            setError(errorMessage(deleteError, "The role could not be deleted."));
                          }
                        }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <FormDialog
        open={creating}
        title="New role"
        submitLabel="Create role"
        disabled={!name.trim() || !project.trim()}
        onClose={() => {
          setCreating(false);
          setName("");
          setProject("");
          setReadOnly(true);
        }}
        onSubmit={async () => {
          await createRole(name.trim(), project.trim(), environment, readOnly);
          await refresh();
        }}
      >
        <TextField
          label="Role name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          helperText="Letters, digits, dashes and underscores."
          autoFocus
          required
        />
        <TextField
          label="Project"
          select={(projects.data ?? []).length > 0}
          value={project}
          onChange={(event) => setProject(event.target.value)}
          required
        >
          {(projects.data ?? []).map((option) => (
            <MenuItem key={option.id} value={option.id}>
              {option.id}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          label="Environment"
          select
          value={environment}
          onChange={(event) => setEnvironment(event.target.value)}
        >
          {ENVIRONMENTS.map((option) => (
            <MenuItem key={option} value={option}>
              {option}
            </MenuItem>
          ))}
        </TextField>
        <FormControlLabel
          control={
            <Switch checked={readOnly} onChange={(event) => setReadOnly(event.target.checked)} />
          }
          label="Read only"
        />
        <Alert severity="info">
          The policy is generated from these fields. Nobody submits raw policy text.
        </Alert>
      </FormDialog>
    </>
  );
}
