"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  IconButton,
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
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import UpIcon from "@mui/icons-material/ArrowUpwardOutlined";
import DownIcon from "@mui/icons-material/ArrowDownwardOutlined";
import { AccessDenied, LoadingRow, PageHeader, isAdmin, useSession } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import {
  addEnvironment,
  errorMessage,
  listAdminProjects,
  removeEnvironment,
  updateEnvironment,
  type EnvironmentSummary,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";

export default function ProjectSettingsPage() {
  const project = String(useParams<{ project: string }>().project);
  const session = useSession();
  const queryClient = useQueryClient();
  const [error, setError] = useState("");
  const [creating, setCreating] = useState(false);
  const [id, setId] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [isProtected, setIsProtected] = useState(false);
  const [renaming, setRenaming] = useState<EnvironmentSummary | null>(null);
  const [renameTo, setRenameTo] = useState("");

  const projects = useQuery({ queryKey: keys.projects, queryFn: listAdminProjects, retry: false });
  const known = projects.data?.find((candidate) => candidate.id === project);
  const environments = known?.environments ?? [];

  if (!isAdmin(session)) return <AccessDenied what="project settings" />;

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.projects });

  async function act(action: () => Promise<unknown>) {
    setError("");
    try {
      await action();
      await refresh();
    } catch (actionError) {
      setError(errorMessage(actionError, "That did not work."));
    }
  }

  // Swapping positions with the neighbour is the whole reorder: the list is rendered
  // in position order, so two updates are enough.
  async function move(environment: EnvironmentSummary, direction: -1 | 1) {
    const ordered = [...environments].sort((a, b) => Number(a.position) - Number(b.position));
    const index = ordered.findIndex((candidate) => candidate.id === environment.id);
    const neighbour = ordered[index + direction];
    if (!neighbour) return;

    await act(async () => {
      await updateEnvironment(project, environment.id, { position: Number(neighbour.position) });
      await updateEnvironment(project, neighbour.id, { position: Number(environment.position) });
    });
  }

  return (
    <>
      <PageHeader
        title={`${project} settings`}
        description="Environments are the top level inside a project. Every secret lives in exactly one."
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New environment
          </Button>
        }
      />

      {error && (
        <Alert severity="error" onClose={() => setError("")} sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Paper sx={{ overflow: "hidden" }}>
        {projects.isLoading ? (
          <LoadingRow label="Loading environments…" />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell width={60}>Order</TableCell>
                <TableCell>Environment</TableCell>
                <TableCell>Name</TableCell>
                <TableCell width={190}>Changes</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {[...environments]
                .sort((a, b) => Number(a.position) - Number(b.position))
                .map((environment, index, all) => (
                  <TableRow key={environment.id} hover>
                    <TableCell>
                      <Stack direction="row">
                        <IconButton
                          size="small"
                          disabled={index === 0}
                          aria-label={`Move ${environment.displayName} up`}
                          onClick={() => void move(environment, -1)}
                        >
                          <UpIcon fontSize="small" />
                        </IconButton>
                        <IconButton
                          size="small"
                          disabled={index === all.length - 1}
                          aria-label={`Move ${environment.displayName} down`}
                          onClick={() => void move(environment, 1)}
                        >
                          <DownIcon fontSize="small" />
                        </IconButton>
                      </Stack>
                    </TableCell>
                    <TableCell>
                      <EnvironmentChip environment={environment.id} />
                    </TableCell>
                    <TableCell>{environment.displayName}</TableCell>
                    <TableCell>
                      <FormControlLabel
                        control={
                          <Switch
                            size="small"
                            checked={environment.protected}
                            onChange={(event) =>
                              void act(() =>
                                updateEnvironment(project, environment.id, {
                                  protected: event.target.checked,
                                }),
                              )
                            }
                          />
                        }
                        label={
                          <Typography variant="caption">
                            {environment.protected ? "Need approval" : "Apply directly"}
                          </Typography>
                        }
                      />
                    </TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={1} justifyContent="flex-end">
                        <Button
                          size="small"
                          onClick={() => {
                            setRenaming(environment);
                            setRenameTo(environment.displayName);
                          }}
                        >
                          Rename
                        </Button>
                        <Tooltip
                          title={
                            all.length === 1
                              ? "A project needs at least one environment"
                              : "Remove environment"
                          }
                        >
                          <span>
                            <IconButton
                              size="small"
                              color="error"
                              disabled={all.length === 1}
                              onClick={() =>
                                void act(async () => {
                                  try {
                                    await removeEnvironment(project, environment.id, false);
                                  } catch (refusal) {
                                    // The server refuses while secrets remain and says
                                    // how many. Repeat that before asking to destroy.
                                    const message = errorMessage(refusal, "");
                                    if (
                                      !message.includes("still holds") ||
                                      !window.confirm(
                                        `${message}\n\nDestroy them and remove the environment?`,
                                      )
                                    ) {
                                      throw refusal;
                                    }

                                    await removeEnvironment(project, environment.id, true);
                                  }
                                })
                              }
                            >
                              <DeleteIcon fontSize="small" />
                            </IconButton>
                          </span>
                        </Tooltip>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <Box sx={{ mt: 2 }}>
        <Chip
          size="small"
          variant="outlined"
          label="Viewer and editor roles are created automatically"
        />
      </Box>

      <FormDialog
        open={creating}
        title="New environment"
        submitLabel="Create environment"
        disabled={!id.trim()}
        onClose={() => {
          setCreating(false);
          setId("");
          setDisplayName("");
          setIsProtected(false);
        }}
        onSubmit={async () => {
          await addEnvironment(project, {
            id: id.trim(),
            displayName: displayName.trim() || id.trim(),
            protected: isProtected,
          });
          await refresh();
        }}
      >
        <TextField
          label="Identifier"
          value={id}
          onChange={(event) => setId(event.target.value)}
          helperText="Used in secret paths and role names. Letters, digits, dashes and underscores."
          autoFocus
          required
        />
        <TextField
          label="Display name"
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          helperText="Optional. Defaults to the identifier."
        />
        <FormControlLabel
          control={
            <Switch
              checked={isProtected}
              onChange={(event) => setIsProtected(event.target.checked)}
            />
          }
          label="Changes need approval"
        />
      </FormDialog>

      <FormDialog
        open={renaming !== null}
        title={`Rename ${renaming?.id ?? ""}`}
        submitLabel="Save name"
        onClose={() => setRenaming(null)}
        onSubmit={async () => {
          if (!renaming) return;
          await updateEnvironment(project, renaming.id, { displayName: renameTo.trim() });
          await refresh();
        }}
      >
        <TextField
          label="Display name"
          value={renameTo}
          onChange={(event) => setRenameTo(event.target.value)}
          autoFocus
        />
        <Alert severity="info">
          Only the label changes. The identifier stays the same, so secret paths and role names are
          untouched.
        </Alert>
      </FormDialog>
    </>
  );
}
