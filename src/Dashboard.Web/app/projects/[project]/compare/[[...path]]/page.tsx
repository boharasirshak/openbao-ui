"use client";

import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
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
import LockIcon from "@mui/icons-material/LockOutlined";
import VisibilityIcon from "@mui/icons-material/VisibilityOutlined";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOffOutlined";
import OpenIcon from "@mui/icons-material/OpenInNewOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { CopyButton } from "@/components/SecretValue";
import { buildComparison, type Cell } from "@/lib/compare";
import { compareEnvironments, errorMessage } from "@/lib/client";
import { keys } from "@/lib/queryKeys";
import { mono } from "@/lib/theme";

export default function ComparePage() {
  const params = useParams<{ project: string; path?: string[] }>();
  const project = String(params.project);
  const path = (params.path ?? []).map(String).join("/");
  const router = useRouter();
  const search = useSearchParams();

  const [onlyDifferences, setOnlyDifferences] = useState(search.get("only") === "diff");
  const [filter, setFilter] = useState("");
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [revealAll, setRevealAll] = useState(false);
  const [pathDraft, setPathDraft] = useState(path);

  const comparison = useQuery({
    queryKey: [...keys.project(project), "compare", path],
    queryFn: () => compareEnvironments(project, path),
    enabled: path.length > 0,
  });

  const built = useMemo(
    () => (comparison.data ? buildComparison(comparison.data.environments) : null),
    [comparison.data],
  );

  const rows = useMemo(() => {
    if (!built) return [];
    const needle = filter.trim().toLowerCase();
    return built.rows.filter(
      (row) =>
        (!onlyDifferences || row.status !== "identical") &&
        (needle.length === 0 || row.key.toLowerCase().includes(needle)),
    );
  }, [built, filter, onlyDifferences]);

  if (!path) {
    return (
      <>
        <PageHeader
          title="Compare environments"
          description="See one secret path side by side across every environment."
        />
        <Paper sx={{ p: 3, maxWidth: 520 }}>
          <Stack
            component="form"
            spacing={2}
            onSubmit={(event) => {
              event.preventDefault();
              const clean = pathDraft.split("/").filter(Boolean).map(encodeURIComponent).join("/");
              if (clean) router.push(`/projects/${encodeURIComponent(project)}/compare/${clean}`);
            }}
          >
            <TextField
              label="Secret path"
              value={pathDraft}
              onChange={(event) => setPathDraft(event.target.value)}
              helperText="For example backend or services/api."
              autoFocus
            />
            <Button type="submit" variant="contained" disabled={!pathDraft.trim()}>
              Compare
            </Button>
          </Stack>
        </Paper>
      </>
    );
  }

  const columns = comparison.data?.environments.map((snapshot) => snapshot.environment) ?? [];

  return (
    <>
      <PageHeader
        title={
          <Stack direction="row" spacing={1.5} alignItems="center">
            <Typography variant="h5" sx={{ fontFamily: mono }}>
              {path}
            </Typography>
          </Stack>
        }
        description={`Compared across ${columns.length} environment(s) in ${project}`}
        actions={
          <>
            <TextField
              placeholder="Filter keys"
              value={filter}
              onChange={(event) => setFilter(event.target.value)}
              sx={{ width: 180 }}
            />
            <Button
              startIcon={revealAll ? <VisibilityOffIcon /> : <VisibilityIcon />}
              onClick={() => {
                setRevealAll(!revealAll);
                setRevealed({});
              }}
            >
              {revealAll ? "Hide all" : "Reveal all"}
            </Button>
          </>
        }
      />

      <FormControlLabel
        sx={{ mb: 2 }}
        control={
          <Switch
            checked={onlyDifferences}
            onChange={(event) => setOnlyDifferences(event.target.checked)}
          />
        }
        label="Only show differences"
      />

      {comparison.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(comparison.error, "The comparison could not be loaded.")}
        </Alert>
      )}

      <Paper sx={{ overflowX: "auto" }}>
        {comparison.isLoading ? (
          <LoadingRow label="Reading every environment…" />
        ) : built && built.rows.length === 0 ? (
          <EmptyState title="No keys at this path" hint="No environment holds a secret here yet." />
        ) : rows.length === 0 ? (
          <EmptyState title="Every key matches" hint="Turn off the filter to see them all." />
        ) : (
          <Table size="small">
            <caption className="sr-only" style={{ position: "absolute", left: -9999 }}>
              Secret values at {path} compared across environments
            </caption>
            <TableHead>
              <TableRow>
                <TableCell component="th" scope="col" sx={{ minWidth: 180 }}>
                  Key
                </TableCell>
                {comparison.data?.environments.map((snapshot) => (
                  <TableCell
                    key={snapshot.environment}
                    component="th"
                    scope="col"
                    sx={{
                      minWidth: 200,
                      // Production carries its own visual weight, not colour alone.
                      borderTop: snapshot.environment === "production" ? "2px solid" : undefined,
                      borderTopColor: "error.main",
                    }}
                  >
                    <Stack direction="row" spacing={1} alignItems="center">
                      <EnvironmentChip environment={snapshot.environment} />
                      {!snapshot.accessible && (
                        <Chip
                          size="small"
                          icon={<LockIcon fontSize="small" />}
                          label="No access"
                          variant="outlined"
                        />
                      )}
                      {snapshot.accessible && !snapshot.exists && (
                        <Chip size="small" variant="outlined" label="not created" />
                      )}
                      {snapshot.accessible && snapshot.exists && (
                        <Tooltip title="Open in the editor">
                          <Link
                            href={`/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(snapshot.environment)}/secrets/${path
                              .split("/")
                              .map(encodeURIComponent)
                              .join("/")}`}
                          >
                            <OpenIcon fontSize="small" sx={{ color: "text.secondary" }} />
                          </Link>
                        </Tooltip>
                      )}
                    </Stack>
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={row.key} hover>
                  <TableCell component="th" scope="row">
                    <Stack direction="row" spacing={1} alignItems="center">
                      <StatusMark status={row.status} />
                      <Box
                        sx={{
                          fontFamily: mono,
                          fontSize: 13,
                          color: row.status === "identical" ? "text.secondary" : "text.primary",
                          wordBreak: "break-all",
                        }}
                      >
                        {row.key}
                      </Box>
                    </Stack>
                  </TableCell>
                  {columns.map((environment) => (
                    <TableCell key={environment}>
                      <ValueCell
                        cell={row.cells[environment]}
                        label={`${row.key} in ${environment}`}
                        revealed={revealAll || Boolean(revealed[`${row.key}:${environment}`])}
                        onToggle={() =>
                          setRevealed((current) => ({
                            ...current,
                            [`${row.key}:${environment}`]: !(
                              revealAll || current[`${row.key}:${environment}`]
                            ),
                          }))
                        }
                      />
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      {built && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
          {built.rows.filter((row) => row.status === "differs").length} differ ·{" "}
          {built.rows.filter((row) => row.status === "partial").length} not set everywhere ·{" "}
          {built.identicalCount} identical
          {built.locked.length > 0 && ` · ${built.locked.join(", ")} excluded, no access`}
        </Typography>
      )}
    </>
  );
}

function StatusMark({ status }: { status: "identical" | "differs" | "partial" }) {
  // Shape and text, never colour on its own.
  if (status === "differs") {
    return (
      <Tooltip title="Values differ">
        <Box component="span" sx={{ color: "warning.main", fontSize: 12 }} aria-label="differs">
          ◆
        </Box>
      </Tooltip>
    );
  }
  if (status === "partial") {
    return (
      <Tooltip title="Not set in every environment">
        <Box component="span" sx={{ color: "text.secondary", fontSize: 12 }} aria-label="partial">
          ⊘
        </Box>
      </Tooltip>
    );
  }
  return <Box component="span" sx={{ width: 12 }} aria-hidden />;
}

function ValueCell({
  cell,
  label,
  revealed,
  onToggle,
}: {
  cell: Cell;
  label: string;
  revealed: boolean;
  onToggle: () => void;
}) {
  if (cell.kind === "locked") {
    return (
      <Typography variant="body2" color="text.disabled" aria-label={`No access to ${label}`}>
        —
      </Typography>
    );
  }

  if (cell.kind === "absent" || cell.kind === "missing") {
    return (
      <Typography
        variant="body2"
        sx={{
          color: "text.disabled",
          fontStyle: "italic",
          border: "1px dashed",
          borderColor: "divider",
          borderRadius: 1,
          px: 1,
          display: "inline-block",
        }}
        aria-label={`${label} is not set`}
      >
        not set
      </Typography>
    );
  }

  const value = cell.kind === "empty" ? "" : cell.value;

  return (
    <Stack direction="row" spacing={0.5} alignItems="center">
      <Box
        component="span"
        sx={{ fontFamily: mono, fontSize: 13, flex: 1, wordBreak: "break-all" }}
        aria-label={`${label}, ${revealed ? "shown" : "hidden"}, group ${cell.group}`}
      >
        {cell.kind === "empty" ? <em>(empty)</em> : revealed ? value : "••••••••"}
      </Box>
      <Chip size="small" variant="outlined" label={cell.group} sx={{ minWidth: 28 }} />
      <Tooltip title={revealed ? "Hide" : "Reveal"}>
        <Box
          component="button"
          type="button"
          onClick={onToggle}
          aria-pressed={revealed}
          aria-label={`${revealed ? "Hide" : "Reveal"} ${label}`}
          sx={{
            background: "none",
            border: "none",
            cursor: "pointer",
            color: "text.secondary",
            display: "flex",
            p: 0.5,
          }}
        >
          {revealed ? <VisibilityOffIcon fontSize="small" /> : <VisibilityIcon fontSize="small" />}
        </Box>
      </Tooltip>
      {cell.kind === "value" && <CopyButton value={value} title={`Copy ${label}`} />}
    </Stack>
  );
}
