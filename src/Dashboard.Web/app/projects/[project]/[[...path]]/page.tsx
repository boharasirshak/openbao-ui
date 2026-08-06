"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { useQueries } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  Chip,
  MenuItem,
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
import AddIcon from "@mui/icons-material/AddOutlined";
import FolderIcon from "@mui/icons-material/FolderOutlined";
import FileIcon from "@mui/icons-material/DescriptionOutlined";
import LockIcon from "@mui/icons-material/LockOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import FormDialog from "@/components/FormDialog";
import { errorMessage, isForbidden, listSecretEntries } from "@/lib/client";
import { keys as queryKeys } from "@/lib/queryKeys";
import { mono } from "@/lib/theme";
import { useProjectEnvironments } from "@/lib/useProjectEnvironments";

type Presence = "loading" | "yes" | "no" | "locked" | "error";

/**
 * The project home: every folder and secret as a row, every environment as a column,
 * so what's missing where is visible at a glance. Clicking a secret opens the
 * side-by-side value comparison; clicking a cell jumps into that environment's editor.
 * This replaced a page that only listed the environments.
 */
export default function ProjectOverviewPage() {
  const params = useParams<{ project: string; path?: string[] }>();
  const project = String(params.project);
  const segments = (params.path ?? []).map(String);
  const path = segments.join("/");
  const root = `/projects/${encodeURIComponent(project)}`;
  const { description, environments, isLoading, isError, error } = useProjectEnvironments(project);
  const [creating, setCreating] = useState(false);

  const listings = useQueries({
    queries: environments.map((environment) => ({
      queryKey: queryKeys.entries(project, environment.id, path),
      queryFn: () => listSecretEntries(project, environment.id, path),
      retry: false,
    })),
  });

  // Union of everything every environment holds at this path, one row per name.
  const rows = new Map<string, { name: string; isFolder: boolean; presence: Presence[] }>();
  environments.forEach((environment, column) => {
    const listing = listings[column];
    const state: Presence = listing.isLoading
      ? "loading"
      : listing.isError
        ? isForbidden(listing.error)
          ? "locked"
          : "error"
        : "yes";
    for (const entry of listing.data ?? []) {
      const key = `${entry.isFolder ? "f" : "s"}:${entry.name}`;
      if (!rows.has(key)) {
        rows.set(key, {
          name: entry.name,
          isFolder: entry.isFolder,
          presence: environments.map(() => "no"),
        });
      }

      rows.get(key)!.presence[column] = state;
    }
  });
  // Columns that failed outright poison every row the same way, so rows the column
  // never listed still show the lock instead of a false "not set".
  environments.forEach((environment, column) => {
    const listing = listings[column];
    if (listing.isError || listing.isLoading) {
      const state: Presence = listing.isLoading
        ? "loading"
        : isForbidden(listing.error)
          ? "locked"
          : "error";
      for (const row of rows.values()) {
        if (row.presence[column] === "no") {
          row.presence[column] = state;
        }
      }
    }
  });

  const sorted = [...rows.values()].sort((left, right) =>
    left.isFolder === right.isFolder ? left.name.localeCompare(right.name) : left.isFolder ? -1 : 1,
  );
  const stillLoading = isLoading || listings.some((listing) => listing.isLoading);

  return (
    <>
      <PageHeader
        title={
          segments.length === 0 ? (
            project
          ) : (
            <Breadcrumbs sx={{ "& a:hover": { textDecoration: "underline" } }}>
              <Link href={root}>{project}</Link>
              {segments.map((segment, index) =>
                index === segments.length - 1 ? (
                  <Typography key={segment} variant="h5" sx={{ fontFamily: mono }}>
                    {segment}
                  </Typography>
                ) : (
                  <Link
                    key={segment}
                    href={`${root}/${segments
                      .slice(0, index + 1)
                      .map(encodeURIComponent)
                      .join("/")}`}
                  >
                    {segment}
                  </Link>
                ),
              )}
            </Breadcrumbs>
          )
        }
        description={
          segments.length === 0
            ? description || "Everything in this project, across every environment."
            : `Folder ${path}`
        }
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
            New secret
          </Button>
        }
      />

      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(error, "The project could not be loaded.")}
        </Alert>
      )}

      <Paper sx={{ overflowX: "auto" }}>
        {stillLoading && sorted.length === 0 ? (
          <LoadingRow label="Reading every environment…" />
        ) : sorted.length === 0 ? (
          <Stack alignItems="center" spacing={2} sx={{ py: 6 }}>
            <EmptyState
              title={path ? "This folder is empty" : "No secrets anywhere yet"}
              hint="Secrets are values your app needs at runtime, like an API key."
            />
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
              New secret
            </Button>
          </Stack>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell component="th" scope="col" sx={{ minWidth: 220 }}>
                  Name
                </TableCell>
                {environments.map((environment) => (
                  <TableCell
                    key={environment.id}
                    component="th"
                    scope="col"
                    sx={{
                      minWidth: 140,
                      borderTop: environment.protected ? "2px solid" : undefined,
                      borderTopColor: "warning.main",
                    }}
                  >
                    <Stack direction="row" spacing={0.75} alignItems="center">
                      <EnvironmentChip environment={environment.id} />
                      {environment.protected && (
                        <Tooltip title="Changes need approval">
                          <ShieldIcon sx={{ fontSize: 14, color: "warning.main" }} />
                        </Tooltip>
                      )}
                    </Stack>
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {sorted.map((row) => {
                const rowPath = [...segments, row.name].map(encodeURIComponent).join("/");
                return (
                  <TableRow key={`${row.isFolder}:${row.name}`} hover>
                    <TableCell component="th" scope="row">
                      <Box
                        component={Link}
                        href={row.isFolder ? `${root}/${rowPath}` : `${root}/compare/${rowPath}`}
                        sx={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 1,
                          fontFamily: mono,
                          fontSize: 13,
                          "&:hover": { color: "primary.main" },
                        }}
                      >
                        {row.isFolder ? (
                          <FolderIcon fontSize="small" sx={{ color: "primary.main" }} />
                        ) : (
                          <FileIcon fontSize="small" sx={{ color: "text.secondary" }} />
                        )}
                        {row.name}
                      </Box>
                    </TableCell>
                    {environments.map((environment, column) => (
                      <TableCell key={environment.id}>
                        <PresenceCell
                          presence={row.presence[column]}
                          href={
                            row.isFolder
                              ? `${root}/${rowPath}`
                              : `${root}/environments/${encodeURIComponent(environment.id)}/secrets/${rowPath}`
                          }
                          label={`${row.name} in ${environment.id}`}
                        />
                      </TableCell>
                    ))}
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </Paper>

      {sorted.length > 0 && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
          Click a secret's name to compare its values side by side, or a cell to edit it in that
          environment.
        </Typography>
      )}

      <NewSecretDialog
        open={creating}
        project={project}
        folder={path}
        environments={environments.map((environment) => environment.id)}
        onClose={() => setCreating(false)}
      />
    </>
  );
}

function PresenceCell({
  presence,
  href,
  label,
}: {
  presence: Presence;
  href: string;
  label: string;
}) {
  if (presence === "loading") {
    return (
      <Typography variant="body2" color="text.disabled" aria-label={`${label} is loading`}>
        …
      </Typography>
    );
  }

  if (presence === "locked") {
    return (
      <Tooltip title="No access">
        <LockIcon
          sx={{ fontSize: 14, color: "text.disabled" }}
          aria-label={`No access to ${label}`}
        />
      </Tooltip>
    );
  }

  if (presence === "error") {
    return (
      <Typography variant="body2" color="error" aria-label={`${label} could not be read`}>
        !
      </Typography>
    );
  }

  if (presence === "no") {
    return (
      <Chip
        size="small"
        variant="outlined"
        label="not set"
        component={Link}
        href={href}
        clickable
        sx={{ borderStyle: "dashed", color: "text.disabled" }}
        aria-label={`${label} is not set — create it`}
      />
    );
  }

  return (
    <Chip
      size="small"
      color="success"
      variant="outlined"
      label="set"
      component={Link}
      href={href}
      clickable
      aria-label={`${label} is set — open it`}
    />
  );
}

/** Names a new secret and picks which environment it starts in. */
function NewSecretDialog({
  open,
  project,
  folder,
  environments,
  onClose,
}: {
  open: boolean;
  project: string;
  folder: string;
  environments: string[];
  onClose: () => void;
}) {
  const router = useRouter();
  const [name, setName] = useState("");
  const [environment, setEnvironment] = useState(environments[0] ?? "development");

  const segments = name.split("/").filter(Boolean);
  const valid =
    segments.length > 0 && segments.every((segment) => /^[A-Za-z0-9_-]+$/.test(segment));

  return (
    <FormDialog
      open={open}
      title="New secret"
      submitLabel="Create"
      disabled={!valid || !environment}
      onClose={() => {
        setName("");
        onClose();
      }}
      onSubmit={() => {
        const full = [...folder.split("/").filter(Boolean), ...segments]
          .map(encodeURIComponent)
          .join("/");
        router.push(
          `/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/${full}`,
        );
      }}
    >
      <TextField
        select
        label="Environment"
        value={environment}
        onChange={(event) => setEnvironment(event.target.value)}
        helperText="Where it starts. You can copy it to the others from the compare view."
      >
        {environments.map((option) => (
          <MenuItem key={option} value={option}>
            {option}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        label="Name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        placeholder="checkout-api"
        helperText="Use a slash to put it in a folder, like billing/stripe."
        error={name.length > 0 && !valid}
        autoFocus
        fullWidth
      />
    </FormDialog>
  );
}
