"use client";

import { useState } from "react";
import {
  Alert,
  Autocomplete,
  Chip,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import FormDialog from "@/components/FormDialog";
import { CopyButton } from "@/components/SecretValue";
import { createShare } from "@/lib/client";
import { mono } from "@/lib/theme";

const TTL_CHOICES = [
  { label: "15 minutes", value: 900 },
  { label: "1 hour", value: 3600 },
  { label: "24 hours", value: 86400 },
  { label: "7 days", value: 604800 },
];

export default function ShareLinkDialog({
  open,
  onClose,
  values,
}: {
  open: boolean;
  onClose: () => void;
  values: Record<string, string>;
}) {
  const [selected, setSelected] = useState<string[]>([]);
  const [ttl, setTtl] = useState(3600);
  const [link, setLink] = useState<{ url: string; expiresAt: string } | null>(null);

  const keys = Object.keys(values);
  const chosen = selected.length > 0 ? selected : keys;

  // Two independent dialogs rather than one with a branch. FormDialog closes itself
  // after a successful submit, so the result has to live in a dialog whose visibility
  // does not depend on the caller's `open`.
  return (
    <>
      <FormDialog
        open={open && link === null}
        title="Share once"
        submitLabel="Create link"
        disabled={keys.length === 0}
        onClose={() => {
          setSelected([]);
          onClose();
        }}
        onSubmit={async () => {
          const payload = Object.fromEntries(chosen.map((key) => [key, values[key]]));
          const created = await createShare(payload, ttl);
          setLink({
            url: `${window.location.origin}/share/${created.token}`,
            expiresAt: created.expiresAt,
          });
        }}
      >
        <Alert severity="info">
          OpenBao holds the values behind a single-use token. Nothing is copied into this
          application, and the link dies the first time it is opened.
        </Alert>

        <Autocomplete
          multiple
          options={keys}
          value={chosen}
          onChange={(_, next) => setSelected(next)}
          renderValue={(value, getItemProps) =>
            value.map((key, index) => (
              <Chip size="small" label={key} {...getItemProps({ index })} key={key} />
            ))
          }
          renderInput={(params) => (
            <TextField {...params} label="Keys to share" helperText="Defaults to all of them." />
          )}
        />

        <TextField
          select
          label="Link expires after"
          value={ttl}
          onChange={(event) => setTtl(Number(event.target.value))}
        >
          {TTL_CHOICES.map((choice) => (
            <MenuItem key={choice.value} value={choice.value}>
              {choice.label}
            </MenuItem>
          ))}
        </TextField>
      </FormDialog>

      <FormDialog
        open={link !== null}
        title="Share link created"
        submitLabel="Done"
        onClose={() => {
          setSelected([]);
          setLink(null);
          onClose();
        }}
        onSubmit={() => {
          setSelected([]);
          setLink(null);
          onClose();
        }}
      >
        <Alert severity="warning">
          Anyone with this link can read the values once. Send it through a channel you trust.
        </Alert>
        <Paper variant="outlined" sx={{ p: 1.5 }}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography sx={{ fontFamily: mono, fontSize: 12, wordBreak: "break-all", flex: 1 }}>
              {link?.url}
            </Typography>
            <CopyButton value={link?.url ?? ""} title="Copy the link" />
          </Stack>
        </Paper>
        <Typography variant="caption" color="text.secondary">
          {link && `Expires ${new Date(link.expiresAt).toLocaleString()}`}, or as soon as it is
          opened — whichever comes first.
        </Typography>
      </FormDialog>
    </>
  );
}
