"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/AddOutlined";
import KeyIcon from "@mui/icons-material/VpnKeyOutlined";
import BlockIcon from "@mui/icons-material/BlockOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import { CopyButton } from "@/components/SecretValue";
import { mono } from "@/lib/theme";
import {
  createMachineIdentity,
  errorMessage,
  generateMachineSecretId,
  listAdminProjects,
  listMachineIdentities,
  revokeMachineSecretIds,
} from "@/lib/client";

const ENVIRONMENTS = ["development", "staging", "production"];

export default function MachineIdentitiesPage() {
  const queryClient = useQueryClient();
  const identities = useQuery({ queryKey: ["machine-identities"], queryFn: listMachineIdentities });
  const projects = useQuery({ queryKey: ["projects"], queryFn: listAdminProjects });

  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState("");
  const [project, setProject] = useState("");
  const [environment, setEnvironment] = useState("development");
  const [readOnly, setReadOnly] = useState(true);
  const [ttl, setTtl] = useState("300");
  const [uses, setUses] = useState("1");
  const [issued, setIssued] = useState<{ role: string; secretId: string } | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["machine-identities"] });

  async function issueSecretId(roleName: string) {
    setError("");
    try {
      setIssued({ role: roleName, secretId: await generateMachineSecretId(roleName) });
    } catch (issueError) {
      setError(errorMessage(issueError, "A secret ID could not be generated."));
    }
  }

  return (
    <>
      <PageHeader
        title="Machine identities"
        description="AppRole logins for CI and services. Each one is scoped to a single project and environment."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New identity
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}
      {identities.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(identities.error, "The machine identity list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {identities.isLoading ? (
          <LoadingRow label="Loading machine identities…" />
        ) : (identities.data ?? []).length === 0 ? (
          <EmptyState
            title="No machine identities yet"
            hint="Create one for each pipeline or service that needs secrets."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Role ID</TableCell>
                <TableCell>Scope</TableCell>
                <TableCell>Access</TableCell>
                <TableCell>Token</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(identities.data ?? []).map((identity) => (
                <TableRow key={identity.name} hover>
                  <TableCell sx={{ fontFamily: mono, fontSize: 13 }}>{identity.name}</TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.5} alignItems="center">
                      <Typography
                        sx={{ fontFamily: mono, fontSize: 12, color: "text.secondary" }}
                        noWrap
                      >
                        {identity.roleId}
                      </Typography>
                      <CopyButton value={identity.roleId} title="Copy role ID" />
                    </Stack>
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Typography variant="body2">{identity.project}</Typography>
                      <EnvironmentChip environment={identity.environment} />
                    </Stack>
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      variant="outlined"
                      color={identity.readOnly ? "default" : "warning"}
                      label={identity.readOnly ? "read only" : "read and write"}
                    />
                  </TableCell>
                  <TableCell sx={{ color: "text.secondary", fontSize: 12 }}>
                    {identity.tokenTtlSeconds ?? "—"}s · {identity.tokenUses ?? "—"} use(s)
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" justifyContent="flex-end">
                      <Tooltip title="Generate a secret ID">
                        <IconButton size="small" onClick={() => issueSecretId(identity.name)}>
                          <KeyIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Revoke every secret ID">
                        <IconButton
                          size="small"
                          color="error"
                          onClick={async () => {
                            if (
                              !window.confirm(
                                `Revoke all secret IDs for "${identity.name}"? Anything using them stops working immediately.`,
                              )
                            ) {
                              return;
                            }
                            setError("");
                            try {
                              await revokeMachineSecretIds(identity.name);
                              await refresh();
                            } catch (revokeError) {
                              setError(
                                errorMessage(revokeError, "The secret IDs could not be revoked."),
                              );
                            }
                          }}
                        >
                          <BlockIcon fontSize="small" />
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
        title="New machine identity"
        submitLabel="Create identity"
        disabled={!name.trim() || !project.trim() || !Number(ttl) || !Number(uses)}
        onClose={() => {
          setCreating(false);
          setName("");
          setProject("");
        }}
        onSubmit={async () => {
          await createMachineIdentity({
            name: name.trim(),
            project: project.trim(),
            environment,
            readOnly,
            tokenTtlSeconds: Number(ttl),
            tokenUses: Number(uses),
          });
          await refresh();
        }}
      >
        <TextField
          label="Name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          helperText="Also used as the AppRole name."
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
        <Stack direction="row" spacing={2}>
          <TextField
            label="Token lifetime (seconds)"
            type="number"
            value={ttl}
            onChange={(event) => setTtl(event.target.value)}
            fullWidth
          />
          <TextField
            label="Token uses"
            type="number"
            value={uses}
            onChange={(event) => setUses(event.target.value)}
            fullWidth
          />
        </Stack>
        <FormControlLabel
          control={
            <Switch checked={readOnly} onChange={(event) => setReadOnly(event.target.checked)} />
          }
          label="Read only"
        />
        <Alert severity="info">
          Short lifetimes and single-use tokens keep a leaked credential nearly worthless.
        </Alert>
      </FormDialog>

      <Dialog open={issued !== null} onClose={() => setIssued(null)} fullWidth maxWidth="sm">
        <DialogTitle>Secret ID for {issued?.role}</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2 }}>
            Copy this now. It is not shown again.
          </Alert>
          <Paper variant="outlined" sx={{ p: 1.5 }}>
            <Stack direction="row" spacing={1} alignItems="center">
              <Typography sx={{ fontFamily: mono, fontSize: 13, wordBreak: "break-all", flex: 1 }}>
                {issued?.secretId}
              </Typography>
              <CopyButton value={issued?.secretId ?? ""} title="Copy secret ID" />
            </Stack>
          </Paper>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button variant="contained" onClick={() => setIssued(null)}>
            Done
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
