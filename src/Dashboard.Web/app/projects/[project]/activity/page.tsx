"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Chip,
  IconButton,
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
import RefreshIcon from "@mui/icons-material/RefreshOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { errorMessage, listActivity } from "@/lib/client";
import { keys } from "@/lib/queryKeys";
import { mono } from "@/lib/theme";

const RANGES = [
  { label: "Last 7 days", value: 7 },
  { label: "Last 14 days", value: 14 },
  { label: "Last 30 days", value: 30 },
  { label: "Last 90 days", value: 90 },
];

/** Plain English, because the enum name is not what a person wants to read. */
const ACTIONS: Record<string, { label: string; tone: "default" | "warning" | "error" }> = {
  SecretSaved: { label: "saved", tone: "default" },
  SecretImported: { label: "imported", tone: "default" },
  SecretDeleted: { label: "deleted", tone: "warning" },
  SecretRestored: { label: "rolled back", tone: "default" },
  SecretUndeleted: { label: "undeleted", tone: "default" },
  VersionDestroyed: { label: "destroyed a version", tone: "error" },
  SecretPurged: { label: "destroyed", tone: "error" },
  SecretShared: { label: "shared", tone: "warning" },
  FolderDeleted: { label: "deleted a folder", tone: "warning" },
  ChangeProposed: { label: "asked for a change", tone: "default" },
  ChangeApplied: { label: "approved a change", tone: "warning" },
  ChangeRejected: { label: "rejected a change", tone: "default" },
  ChangeWithdrawn: { label: "withdrew a change", tone: "default" },
};

export default function ActivityPage() {
  const project = String(useParams<{ project: string }>().project);
  const queryClient = useQueryClient();
  const [days, setDays] = useState(14);

  const activity = useQuery({
    queryKey: [...keys.project(project), "activity", days],
    queryFn: () => listActivity(project, days),
  });

  return (
    <>
      <PageHeader
        title="Activity"
        description="Who changed what in this project. Key names are recorded; values never are."
        actions={
          <>
            <TextField
              select
              value={days}
              onChange={(event) => setDays(Number(event.target.value))}
              sx={{ width: 160 }}
              label="Range"
            >
              {RANGES.map((range) => (
                <MenuItem key={range.value} value={range.value}>
                  {range.label}
                </MenuItem>
              ))}
            </TextField>
            <Tooltip title="Reload">
              <IconButton
                aria-label="Reload"
                onClick={() =>
                  queryClient.invalidateQueries({
                    queryKey: [...keys.project(project), "activity"],
                  })
                }
              >
                <RefreshIcon />
              </IconButton>
            </Tooltip>
          </>
        }
      />

      {activity.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(activity.error, "The activity feed is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {activity.isLoading ? (
          <LoadingRow label="Reading recent activity…" />
        ) : (activity.data ?? []).length === 0 ? (
          <EmptyState title="Nothing yet" hint="Changes made through this dashboard appear here." />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell width={170}>When</TableCell>
                <TableCell width={150}>Who</TableCell>
                <TableCell width={150}>What</TableCell>
                <TableCell>Where</TableCell>
                <TableCell>Keys</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(activity.data ?? []).map((entry, index) => {
                const action = ACTIONS[entry.action] ?? { label: entry.action, tone: "default" };
                return (
                  <TableRow key={`${entry.at}-${index}`} hover>
                    <TableCell sx={{ color: "text.secondary", fontSize: 12 }}>
                      {new Date(entry.at).toLocaleString()}
                    </TableCell>
                    <TableCell sx={{ fontSize: 12 }}>{entry.actor}</TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        variant={action.tone === "default" ? "outlined" : "filled"}
                        color={action.tone}
                        label={action.label}
                      />
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={1} alignItems="center">
                        {entry.environment && <EnvironmentChip environment={entry.environment} />}
                        {entry.path && entry.environment && (
                          <Box
                            component={Link}
                            href={`/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(entry.environment)}/secrets/${entry.path
                              .split("/")
                              .map(encodeURIComponent)
                              .join("/")}`}
                            sx={{
                              fontFamily: mono,
                              fontSize: 12,
                              "&:hover": { color: "primary.main" },
                            }}
                          >
                            {entry.path}
                          </Box>
                        )}
                      </Stack>
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                        {entry.keysAffected.slice(0, 4).map((key: string) => (
                          <Chip key={key} size="small" variant="outlined" label={key} />
                        ))}
                        {entry.keysAffected.length > 4 && (
                          <Typography variant="caption" color="text.secondary">
                            +{entry.keysAffected.length - 4} more
                          </Typography>
                        )}
                      </Stack>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </Paper>
    </>
  );
}
