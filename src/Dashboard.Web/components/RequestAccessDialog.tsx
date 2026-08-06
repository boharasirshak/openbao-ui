"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Alert, Checkbox, FormControlLabel, Stack, TextField, Typography } from "@mui/material";
import FormDialog from "@/components/FormDialog";
import { accessRequestOptions, errorMessage, submitAccessRequest } from "@/lib/client";

/**
 * Ask for a role on a project, instead of messaging an administrator. When the project
 * is fixed (opened from inside one) the name field is hidden; from the projects list
 * the person types the name they were given.
 */
export default function RequestAccessDialog({
  open,
  project,
  onClose,
  onSent,
}: {
  open: boolean;
  project?: string;
  onClose: () => void;
  onSent: () => void;
}) {
  const [typedProject, setTypedProject] = useState("");
  const [debounced, setDebounced] = useState("");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [reason, setReason] = useState("");

  const target = (project ?? debounced).trim();

  // Waiting a beat before fetching, so typing a name does not fire one request per key.
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(typedProject), 400);
    return () => clearTimeout(handle);
  }, [typedProject]);

  const options = useQuery({
    queryKey: ["access-request-options", target],
    queryFn: () => accessRequestOptions(target),
    enabled: open && target.length > 0,
    retry: false,
  });

  function reset() {
    setTypedProject("");
    setDebounced("");
    setPicked(new Set());
    setReason("");
    onClose();
  }

  return (
    <FormDialog
      open={open}
      title={project ? `Request access to ${project}` : "Request access"}
      submitLabel="Send request"
      disabled={target.length === 0 || picked.size === 0}
      onClose={reset}
      onSubmit={async () => {
        await submitAccessRequest(target, [...picked], reason.trim());
        onSent();
      }}
    >
      {!project && (
        <TextField
          label="Project name"
          value={typedProject}
          onChange={(event) => setTypedProject(event.target.value)}
          helperText="Exactly as your team calls it, like checkout or thorneai."
          autoFocus
          fullWidth
        />
      )}

      {options.isError && target.length > 0 && (
        <Alert severity="warning">
          {errorMessage(options.error, "That project could not be found.")}
        </Alert>
      )}

      {options.data && (
        <Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
            What do you need to do?
          </Typography>
          {options.data.roles.map((role) => (
            <FormControlLabel
              key={role.policy}
              control={
                <Checkbox
                  checked={picked.has(role.policy)}
                  onChange={(event) => {
                    const next = new Set(picked);
                    if (event.target.checked) {
                      next.add(role.policy);
                    } else {
                      next.delete(role.policy);
                    }
                    setPicked(next);
                  }}
                />
              }
              label={role.label}
            />
          ))}
        </Stack>
      )}

      <TextField
        label="Why do you need it?"
        value={reason}
        onChange={(event) => setReason(event.target.value)}
        helperText="Optional, but it is what the approver reads first."
        multiline
        minRows={2}
        fullWidth
      />
    </FormDialog>
  );
}
