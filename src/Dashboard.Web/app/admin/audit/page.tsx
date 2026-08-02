"use client";

import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Chip,
  IconButton,
  MenuItem,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
} from "@mui/material";
import RefreshIcon from "@mui/icons-material/RefreshOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import { mono } from "@/lib/theme";
import { errorMessage, listAuditEvents } from "@/lib/client";

const LIMITS = [50, 100, 250, 500];

export default function AuditPage() {
  const queryClient = useQueryClient();
  const [limit, setLimit] = useState(100);
  const events = useQuery({
    queryKey: ["audit", limit],
    queryFn: () => listAuditEvents(limit),
  });

  return (
    <>
      <PageHeader
        title="Audit log"
        description="Recent activity from the OpenBao audit device. Secret values are never included."
        actions={
          <>
            <TextField
              select
              value={limit}
              onChange={(event) => setLimit(Number(event.target.value))}
              sx={{ width: 130 }}
              label="Show"
            >
              {LIMITS.map((option) => (
                <MenuItem key={option} value={option}>
                  Last {option}
                </MenuItem>
              ))}
            </TextField>
            <Tooltip title="Reload">
              <IconButton
                onClick={() => queryClient.invalidateQueries({ queryKey: ["audit"] })}
                aria-label="Reload"
              >
                <RefreshIcon />
              </IconButton>
            </Tooltip>
          </>
        }
      />

      {events.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(events.error, "The audit log is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {events.isLoading ? (
          <LoadingRow label="Loading audit events…" />
        ) : (events.data ?? []).length === 0 ? (
          <EmptyState
            title="No audit events"
            hint="Configure an OpenBao audit device to start recording activity."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell width={190}>Time</TableCell>
                <TableCell width={110}>Type</TableCell>
                <TableCell width={110}>Operation</TableCell>
                <TableCell>Path</TableCell>
                <TableCell width={180}>Actor</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {(events.data ?? []).map((event, index) => (
                <TableRow key={`${event.time}-${event.path}-${index}`} hover>
                  <TableCell sx={{ color: "text.secondary", fontSize: 12 }}>
                    {event.time ? new Date(event.time).toLocaleString() : "unknown"}
                  </TableCell>
                  <TableCell>
                    <Chip size="small" variant="outlined" label={event.type || "—"} />
                  </TableCell>
                  <TableCell sx={{ fontSize: 12 }}>{event.operation || "—"}</TableCell>
                  <TableCell sx={{ fontFamily: mono, fontSize: 12, wordBreak: "break-all" }}>
                    {event.path || "—"}
                  </TableCell>
                  <TableCell sx={{ fontSize: 12 }}>{event.actor || "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>
    </>
  );
}
