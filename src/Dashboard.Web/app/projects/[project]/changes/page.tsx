"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
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
import CheckIcon from "@mui/icons-material/CheckCircleOutlined";
import CloseIcon from "@mui/icons-material/CancelOutlined";
import UndoIcon from "@mui/icons-material/UndoOutlined";
import VisibilityIcon from "@mui/icons-material/VisibilityOutlined";
import {
  AccessDenied,
  EmptyState,
  LoadingRow,
  PageHeader,
  useSession,
} from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { CopyButton, MaskedValue } from "@/components/SecretValue";
import {
  approveChange,
  errorMessage,
  isForbidden,
  listChanges,
  readProposedValues,
  rejectChange,
  withdrawChange,
  type ChangeRequest,
} from "@/lib/client";
import { keys } from "@/lib/queryKeys";
import { mono } from "@/lib/theme";

const STATUS: Record<string, { label: string; tone: "warning" | "success" | "error" | "default" }> =
  {
    Pending: { label: "Waiting for review", tone: "warning" },
    Applied: { label: "Approved", tone: "success" },
    Rejected: { label: "Rejected", tone: "error" },
    Withdrawn: { label: "Withdrawn", tone: "default" },
  };

export default function ChangesPage() {
  const project = String(useParams<{ project: string }>().project);
  const queryClient = useQueryClient();
  const session = useSession();
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reviewing, setReviewing] = useState<ChangeRequest | null>(null);
  const [inspecting, setInspecting] = useState<ChangeRequest | null>(null);

  const changes = useQuery({
    queryKey: keys.changes(project),
    queryFn: () => listChanges(project),
    retry: false,
  });

  const proposed = useQuery({
    queryKey: [...keys.changes(project), inspecting?.id, "values"],
    queryFn: () => readProposedValues(project, inspecting!.id),
    enabled: inspecting !== null,
    retry: false,
  });

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.changes(project) });

  const review = useMutation({
    mutationFn: (input: { change: ChangeRequest; action: "approve" | "reject"; note: string }) =>
      input.action === "approve"
        ? approveChange(project, input.change.id, input.note || undefined)
        : rejectChange(project, input.change.id, input.note || undefined),
    onSuccess: async (_result, input) => {
      setNotice(
        input.action === "approve"
          ? `Approved. ${input.change.path} in ${input.change.environment} has been updated.`
          : "The change was rejected and nothing was written.",
      );
      setError(null);
      await refresh();
    },
    onError: (failure) => setError(errorMessage(failure, "The review could not be recorded.")),
  });

  const withdraw = useMutation({
    mutationFn: (change: ChangeRequest) => withdrawChange(project, change.id),
    onSuccess: async () => {
      setNotice("Your change was withdrawn.");
      setError(null);
      await refresh();
    },
    onError: (failure) => setError(errorMessage(failure, "The change could not be withdrawn.")),
  });

  if (changes.isError && isForbidden(changes.error)) {
    return <AccessDenied what="the change requests for this project" />;
  }

  const rows = changes.data ?? [];
  const open = rows.filter((change) => change.status === "Pending");

  return (
    <>
      {/* Titled to match the sidebar. It said "Changes" there and "Approvals" here,
          which reads as two different screens. The back link went too — the sidebar
          is always present now. */}
      <PageHeader
        title="Approvals"
        description="Edits to a protected environment wait here until somebody other than the author approves them."
      />

      {notice && (
        <Alert severity="success" sx={{ mb: 2 }} onClose={() => setNotice(null)}>
          {notice}
        </Alert>
      )}
      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}
      {changes.isError && !isForbidden(changes.error) && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(changes.error, "The change list is unavailable.")}
        </Alert>
      )}

      <Paper sx={{ overflow: "auto" }}>
        {changes.isLoading ? (
          <LoadingRow label="Loading changes…" />
        ) : rows.length === 0 ? (
          <EmptyState
            title="No changes waiting"
            hint="Save a secret in a protected environment and it will appear here for review."
          />
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell width={190}>Asked</TableCell>
                <TableCell width={130}>Who</TableCell>
                <TableCell>Where</TableCell>
                <TableCell>Keys</TableCell>
                <TableCell width={170}>Status</TableCell>
                <TableCell width={230} align="right">
                  {open.length > 0 ? `${open.length} waiting` : ""}
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((change) => {
                const status = STATUS[change.status] ?? {
                  label: change.status,
                  tone: "default" as const,
                };
                const mine = change.requestedBy === session?.username;
                return (
                  <TableRow key={change.id} hover>
                    <TableCell sx={{ color: "text.secondary", fontSize: 12 }}>
                      {new Date(change.requestedAt).toLocaleString()}
                      {change.reason && (
                        <Typography variant="caption" display="block" color="text.secondary">
                          “{change.reason}”
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell sx={{ fontSize: 12 }}>
                      {change.requestedBy}
                      {mine && (
                        <Typography variant="caption" display="block" color="text.secondary">
                          you
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={1} alignItems="center">
                        <EnvironmentChip environment={change.environment} />
                        <Box sx={{ fontFamily: mono, fontSize: 12 }}>{change.path}</Box>
                      </Stack>
                    </TableCell>
                    <TableCell>
                      {change.isDeletion ? (
                        <Chip size="small" color="error" label="delete the whole secret" />
                      ) : (
                        <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                          {change.keys.slice(0, 4).map((key) => (
                            <Chip key={key} size="small" variant="outlined" label={key} />
                          ))}
                          {change.keys.length > 4 && (
                            <Typography variant="caption" color="text.secondary">
                              +{change.keys.length - 4} more
                            </Typography>
                          )}
                        </Stack>
                      )}
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        color={status.tone}
                        variant={status.tone === "default" ? "outlined" : "filled"}
                        label={status.label}
                      />
                      {change.reviews.map((entry, index) => (
                        <Typography
                          key={index}
                          variant="caption"
                          display="block"
                          color="text.secondary"
                        >
                          {entry.reviewer}
                          {entry.note ? `: ${entry.note}` : ""}
                        </Typography>
                      ))}
                    </TableCell>
                    <TableCell align="right">
                      {change.status === "Pending" && (
                        <Stack direction="row" spacing={0.5} justifyContent="flex-end">
                          {!change.isDeletion && (
                            <Button
                              size="small"
                              startIcon={<VisibilityIcon />}
                              onClick={() => setInspecting(change)}
                            >
                              Values
                            </Button>
                          )}
                          {change.canReview ? (
                            <Button
                              size="small"
                              startIcon={<CheckIcon />}
                              onClick={() => setReviewing(change)}
                            >
                              Review
                            </Button>
                          ) : mine ? (
                            <Button
                              size="small"
                              color="inherit"
                              startIcon={<UndoIcon />}
                              disabled={withdraw.isPending}
                              onClick={() => withdraw.mutate(change)}
                            >
                              Withdraw
                            </Button>
                          ) : null}
                        </Stack>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </Paper>

      <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: "block" }}>
        Review is enforced by this application, not by OpenBao. Anyone holding a token with write
        access can still change a protected environment by talking to OpenBao directly — this gets a
        second pair of eyes on the normal path.
      </Typography>

      {reviewing && (
        <ReviewDialog
          change={reviewing}
          busy={review.isPending}
          onClose={() => setReviewing(null)}
          onSubmit={(action, note) => {
            const change = reviewing;
            setReviewing(null);
            review.mutate({ change, action, note });
          }}
        />
      )}

      {/* A plain dialog, not FormDialog: this only shows values, so it needs one way
          out. FormDialog always adds Cancel, which left two buttons doing the same job. */}
      <Dialog
        open={inspecting !== null}
        onClose={() => setInspecting(null)}
        fullWidth
        maxWidth="sm"
      >
        <DialogTitle>Proposed values — {inspecting?.path ?? ""}</DialogTitle>
        <DialogContent>
          {proposed.isLoading ? (
            <LoadingRow label="Reading the proposal…" />
          ) : proposed.isError ? (
            <Alert severity="error">
              {errorMessage(proposed.error, "The proposed values could not be read.")}
            </Alert>
          ) : proposed.data === null ? (
            <Alert severity="info">These values are no longer available.</Alert>
          ) : (
            <Stack spacing={1} sx={{ pt: 0.5 }}>
              {Object.entries(proposed.data?.values ?? {}).map(([key, value]) => (
                <ProposedRow key={key} name={key} value={value} />
              ))}
            </Stack>
          )}
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button variant="contained" onClick={() => setInspecting(null)}>
            Close
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

/** One row of a proposal: masked until the reviewer asks to see it. */
function ProposedRow({ name, value }: { name: string; value: string }) {
  const [revealed, setRevealed] = useState(false);
  return (
    <Stack direction="row" spacing={1} alignItems="center">
      <Box sx={{ fontFamily: mono, fontSize: 12, minWidth: 160 }}>{name}</Box>
      <Box sx={{ flex: 1 }}>
        <MaskedValue value={value} revealed={revealed} />
      </Box>
      <Button
        size="small"
        aria-pressed={revealed}
        aria-label={revealed ? `Hide ${name}` : `Reveal ${name}`}
        onClick={() => setRevealed((current) => !current)}
      >
        {revealed ? "Hide" : "Reveal"}
      </Button>
      <CopyButton value={value} title={`Copy ${name}`} />
    </Stack>
  );
}

/**
 * Not FormDialog: this one has two outcomes rather than a single submit, and the
 * difference between them is the whole point of the screen.
 */
function ReviewDialog({
  change,
  busy,
  onClose,
  onSubmit,
}: {
  change: ChangeRequest;
  busy: boolean;
  onClose: () => void;
  onSubmit: (action: "approve" | "reject", note: string) => void;
}) {
  const [note, setNote] = useState("");

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{`Review ${change.path} in ${change.environment}`}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 0.5 }}>
          <Alert severity="warning">
            {change.isDeletion
              ? `Approving removes ${change.path} from ${change.environment}.`
              : `Approving writes ${change.keys.length} key${change.keys.length === 1 ? "" : "s"} to ${change.path} in ${change.environment} straight away.`}
          </Alert>
          {change.reason && (
            <Typography variant="body2" color="text.secondary">
              {change.requestedBy} said: “{change.reason}”
            </Typography>
          )}
          <TextField
            label="Note (optional)"
            value={note}
            onChange={(event) => setNote(event.target.value)}
            multiline
            minRows={2}
            fullWidth
          />
        </Stack>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button
          color="error"
          startIcon={<CloseIcon />}
          disabled={busy}
          onClick={() => onSubmit("reject", note)}
        >
          Reject
        </Button>
        <Button
          variant="contained"
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : <CheckIcon />}
          disabled={busy}
          onClick={() => onSubmit("approve", note)}
        >
          Approve and apply
        </Button>
      </DialogActions>
    </Dialog>
  );
}
