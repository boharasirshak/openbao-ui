"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import { errorMessage, readSecretMetadata, updateSecretMetadata } from "@/lib/client";
import { keys } from "@/lib/queryKeys";

/** Options are suggestions only — any lowercase tag is allowed. */
const COMMON_TAGS = ["billing", "pii", "third-party", "rotate-quarterly", "legacy"];

const RETENTION_CHOICES = [
  { label: "Keep every version", value: 0 },
  { label: "Keep the last 5", value: 5 },
  { label: "Keep the last 10", value: 10 },
  { label: "Keep the last 25", value: 25 },
];

const EXPIRY_CHOICES = [
  { label: "Never expire versions", value: 0 },
  { label: "Delete after 30 days", value: 30 * 86400 },
  { label: "Delete after 90 days", value: 90 * 86400 },
  { label: "Delete after a year", value: 365 * 86400 },
];

export default function SecretMetadataPanel({
  project,
  environment,
  path,
}: {
  project: string;
  environment: string;
  path: string;
}) {
  const queryClient = useQueryClient();
  const metadata = useQuery({
    queryKey: [...keys.secret(project, environment, path), "metadata"],
    queryFn: () => readSecretMetadata(project, environment, path),
  });

  const [tags, setTags] = useState<string[] | null>(null);
  const [comment, setComment] = useState<string | null>(null);
  const [maxVersions, setMaxVersions] = useState<number | null>(null);
  const [expireAfter, setExpireAfter] = useState<number | null>(null);
  const [error, setError] = useState("");

  const stored = metadata.data;
  const currentTags = tags ?? stored?.annotations.tags ?? [];
  const currentComment = comment ?? stored?.annotations.comment ?? "";
  const currentMax = maxVersions ?? stored?.retention.maxVersions ?? 0;
  const currentExpiry = expireAfter ?? stored?.retention.deleteVersionAfterSeconds ?? 0;
  const dirty = tags !== null || comment !== null || maxVersions !== null || expireAfter !== null;

  const save = useMutation({
    mutationFn: () =>
      updateSecretMetadata(project, environment, path, {
        annotations: { description: null, tags: currentTags, comment: currentComment },
        retention: {
          maxVersions: currentMax,
          deleteVersionAfterSeconds: currentExpiry,
        },
      }),
    onSuccess: async () => {
      setTags(null);
      setComment(null);
      setMaxVersions(null);
      setExpireAfter(null);
      setError("");
      await queryClient.invalidateQueries({ queryKey: keys.env(project, environment) });
    },
    onError: (saveError) => setError(errorMessage(saveError, "Those details could not be saved.")),
  });

  if (metadata.isError) {
    return (
      <Alert severity="info">Details are unavailable for this secret. It may not exist yet.</Alert>
    );
  }

  return (
    <Stack spacing={2}>
      <Typography variant="subtitle2">Details</Typography>
      {error && (
        <Alert severity="error" onClose={() => setError("")}>
          {error}
        </Alert>
      )}

      <Autocomplete
        multiple
        freeSolo
        autoSelect
        options={COMMON_TAGS}
        value={currentTags}
        onChange={(_, next) => setTags(next.map((tag) => tag.toLowerCase()))}
        renderValue={(value, getItemProps) =>
          value.map((tag, index) => (
            <Chip size="small" label={tag} {...getItemProps({ index })} key={tag} />
          ))
        }
        renderInput={(params) => (
          <TextField
            {...params}
            label="Tags"
            helperText="Lowercase letters, digits and dashes. Stored as OpenBao metadata, never secret."
          />
        )}
      />

      <TextField
        label="Comment"
        value={currentComment}
        onChange={(event) => setComment(event.target.value)}
        multiline
        minRows={2}
        helperText="Context for whoever reads this next. Never put a secret value here."
      />

      <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
        <TextField
          select
          label="Version history"
          value={currentMax}
          onChange={(event) => setMaxVersions(Number(event.target.value))}
          fullWidth
        >
          {RETENTION_CHOICES.map((choice) => (
            <MenuItem key={choice.value} value={choice.value}>
              {choice.label}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          label="Old versions"
          value={currentExpiry}
          onChange={(event) => setExpireAfter(Number(event.target.value))}
          fullWidth
        >
          {EXPIRY_CHOICES.map((choice) => (
            <MenuItem key={choice.value} value={choice.value}>
              {choice.label}
            </MenuItem>
          ))}
        </TextField>
      </Stack>

      <Stack direction="row" spacing={1} alignItems="center">
        <Button
          variant="contained"
          disabled={!dirty || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? "Saving…" : "Save details"}
        </Button>
        <Button
          disabled={!dirty || save.isPending}
          onClick={() => {
            setTags(null);
            setComment(null);
            setMaxVersions(null);
            setExpireAfter(null);
          }}
        >
          Discard
        </Button>
        <Box sx={{ flex: 1 }} />
        {stored?.updatedAt && (
          <Typography variant="caption" color="text.secondary">
            Updated {new Date(stored.updatedAt).toLocaleString()}
          </Typography>
        )}
      </Stack>
    </Stack>
  );
}
