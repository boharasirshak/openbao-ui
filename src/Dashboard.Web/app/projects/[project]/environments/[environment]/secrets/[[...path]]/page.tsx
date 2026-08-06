"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  Chip,
  Collapse,
  IconButton,
  InputAdornment,
  ListItemIcon,
  ListItemText,
  Menu,
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
import FolderIcon from "@mui/icons-material/FolderOutlined";
import FileIcon from "@mui/icons-material/DescriptionOutlined";
import SearchIcon from "@mui/icons-material/SearchOutlined";
import RefreshIcon from "@mui/icons-material/RefreshOutlined";
import VisibilityIcon from "@mui/icons-material/VisibilityOutlined";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOffOutlined";
import DeleteIcon from "@mui/icons-material/DeleteOutlineOutlined";
import AddIcon from "@mui/icons-material/AddOutlined";
import UploadIcon from "@mui/icons-material/UploadFileOutlined";
import DownloadIcon from "@mui/icons-material/DownloadOutlined";
import HistoryIcon from "@mui/icons-material/HistoryOutlined";
import ShareIcon from "@mui/icons-material/IosShareOutlined";
import CompareIcon from "@mui/icons-material/DifferenceOutlined";
import DeleteForeverIcon from "@mui/icons-material/DeleteForeverOutlined";
import RestoreIcon from "@mui/icons-material/SettingsBackupRestoreOutlined";
import UndoIcon from "@mui/icons-material/UndoOutlined";
import MoreIcon from "@mui/icons-material/MoreVertOutlined";
import ShieldIcon from "@mui/icons-material/VerifiedUserOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import FormDialog from "@/components/FormDialog";
import { CopyButton } from "@/components/SecretValue";
import SecretMetadataPanel from "@/components/secrets/SecretMetadataPanel";
import ShareLinkDialog from "@/components/secrets/ShareLinkDialog";
import { downloadText, isValidKey, parseSecretFile } from "@/lib/dotenv";
import { mono } from "@/lib/theme";
// Aliased: this file already has a local `keys` for the filtered key list.
import { keys as queryKeys } from "@/lib/queryKeys";
import { useProjectEnvironments } from "@/lib/useProjectEnvironments";
import {
  deleteSecret,
  errorMessage,
  needsApproval,
  proposeChange,
  exportSecrets,
  importSecrets,
  listSecretEntries,
  listVersions,
  readSecret,
  restoreSecret,
  undeleteSecret,
  writeSecret,
  destroyVersions,
  purgeSecret,
} from "@/lib/client";

// A stable reference: an inline {} would be a new object on every render and
// would defeat the memoised key list below.
const NO_VALUES: Record<string, string> = {};

/**
 * Names a new secret. KV-v2 has no such thing as an empty folder — a folder exists
 * only because something sits beneath it — so there is deliberately no "new folder"
 * button. Typing a slash here is how you make one.
 */
function NewSecretDialog({
  open,
  base,
  onClose,
}: {
  open: boolean;
  base: string;
  onClose: () => void;
}) {
  const router = useRouter();
  const [name, setName] = useState("");

  const segments = name.split("/").filter(Boolean);
  const valid =
    segments.length > 0 && segments.every((segment) => /^[A-Za-z0-9_-]+$/.test(segment));

  return (
    <FormDialog
      open={open}
      title="New secret"
      submitLabel="Create"
      disabled={!valid}
      onClose={() => {
        setName("");
        onClose();
      }}
      onSubmit={() => {
        router.push(`${base}/${segments.map(encodeURIComponent).join("/")}`);
      }}
    >
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

export default function SecretsPage() {
  const params = useParams<{ project: string; environment: string; path?: string[] }>();
  const project = String(params.project);
  const environment = String(params.environment);
  const segments = (params.path ?? []).map(String);
  const path = segments.join("/");
  const base = `/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets`;

  const queryClient = useQueryClient();
  const [creating, setCreating] = useState(false);
  const { environments } = useProjectEnvironments(project);
  const isProtected =
    environments.find((candidate) => candidate.id === environment)?.protected ?? false;
  const entries = useQuery({
    queryKey: queryKeys.entries(project, environment, path),
    queryFn: () => listSecretEntries(project, environment, path),
  });

  const folders = (entries.data ?? []).filter((entry) => entry.isFolder);
  const documents = (entries.data ?? []).filter((entry) => !entry.isFolder);

  return (
    <>
      <PageHeader
        title={
          <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
            <Breadcrumbs sx={{ "& a:hover": { textDecoration: "underline" } }}>
              <Link href={`/projects/${encodeURIComponent(project)}`}>{project}</Link>
              {segments.length === 0 ? (
                <Typography variant="h5">{environment}</Typography>
              ) : (
                <Link href={base}>{environment}</Link>
              )}
              {segments.map((segment, index) => {
                const href = `${base}/${segments
                  .slice(0, index + 1)
                  .map(encodeURIComponent)
                  .join("/")}`;
                return index === segments.length - 1 ? (
                  <Typography key={href} variant="h5">
                    {segment}
                  </Typography>
                ) : (
                  <Link key={href} href={href}>
                    {segment}
                  </Link>
                );
              })}
            </Breadcrumbs>
          </Stack>
        }
        description={path ? `Path ${path}` : "Top level of this environment"}
        actions={
          <>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
              New secret
            </Button>
            <Tooltip title="Reload">
              <IconButton
                onClick={() =>
                  queryClient.invalidateQueries({ queryKey: queryKeys.env(project, environment) })
                }
                aria-label="Reload"
              >
                <RefreshIcon />
              </IconButton>
            </Tooltip>
          </>
        }
      />

      <NewSecretDialog open={creating} base={base} onClose={() => setCreating(false)} />

      {/* Driven by the environment's own protected flag, not by the name "production".
          It used to say changes here take effect immediately, which is the opposite of
          what a protected environment now does. */}
      {isProtected && (
        <Alert severity="info" icon={<ShieldIcon fontSize="small" />} sx={{ mb: 2 }}>
          Changes here need someone else&apos;s approval before they go live. Save as normal and we
          will send it for review.
        </Alert>
      )}

      {entries.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(entries.error, "This folder could not be listed.")}
        </Alert>
      )}

      {(folders.length > 0 || documents.length > 0 || entries.isLoading) && (
        <Paper sx={{ mb: 3, overflow: "hidden" }}>
          {entries.isLoading ? (
            <LoadingRow label="Listing this folder…" />
          ) : (
            <Table size="small">
              <TableBody>
                {[...folders, ...documents].map((entry) => (
                  <TableRow key={`${entry.isFolder}-${entry.name}`} hover>
                    <TableCell width={44}>
                      {entry.isFolder ? (
                        <FolderIcon fontSize="small" sx={{ color: "primary.main" }} />
                      ) : (
                        <FileIcon fontSize="small" sx={{ color: "text.secondary" }} />
                      )}
                    </TableCell>
                    <TableCell>
                      <Box
                        component={Link}
                        href={`${base}/${[...segments, entry.name].map(encodeURIComponent).join("/")}`}
                        sx={{
                          fontFamily: mono,
                          fontSize: 13,
                          display: "block",
                          "&:hover": { color: "primary.main" },
                        }}
                      >
                        {entry.name}
                      </Box>
                    </TableCell>
                    <TableCell align="right" sx={{ color: "text.secondary", fontSize: 12 }}>
                      {entry.isFolder ? "folder" : "secret"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </Paper>
      )}

      {path ? (
        <SecretEditor
          key={`${project}/${environment}/${path}`}
          project={project}
          environment={environment}
          path={path}
        />
      ) : (
        folders.length === 0 &&
        documents.length === 0 &&
        !entries.isLoading && (
          <Paper>
            <Stack alignItems="center" spacing={2} sx={{ py: 6 }}>
              <EmptyState
                title="No secrets here yet"
                hint="Secrets are values your app needs at runtime, like an API key."
              />
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
                New secret
              </Button>
            </Stack>
          </Paper>
        )
      )}
    </>
  );
}

function SecretEditor({
  project,
  environment,
  path,
}: {
  project: string;
  environment: string;
  path: string;
}) {
  const queryClient = useQueryClient();

  const secret = useQuery({
    queryKey: queryKeys.secret(project, environment, path),
    queryFn: () => readSecret(project, environment, path),
  });

  const [draft, setDraft] = useState<Record<string, string> | null>(null);
  const [draftDescription, setDraftDescription] = useState<string | null>(null);
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [revealAll, setRevealAll] = useState(false);
  const [filter, setFilter] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [adding, setAdding] = useState(false);
  const [newKey, setNewKey] = useState("");
  const [newValue, setNewValue] = useState("");
  const [importing, setImporting] = useState(false);
  const [importText, setImportText] = useState("");
  const [exportMenu, setExportMenu] = useState<HTMLElement | null>(null);
  const [showVersions, setShowVersions] = useState(false);
  const [moreMenu, setMoreMenu] = useState<HTMLElement | null>(null);
  // Set when a protected environment refuses a direct write; holds the edit until the
  // author gives a reason and sends it for review.
  const [pendingReview, setPendingReview] = useState<{
    values: Record<string, string>;
    description?: string;
    deletion: boolean;
  } | null>(null);
  const [reviewReason, setReviewReason] = useState("");
  const [sharing, setSharing] = useState(false);

  const stored = secret.data?.values ?? null;
  const values: Record<string, string> = draft ?? stored ?? NO_VALUES;
  const description = draftDescription ?? secret.data?.description ?? "";
  const currentVersion = Number(secret.data?.version ?? 0);
  const dirty = draft !== null || draftDescription !== null;

  const keys = useMemo(
    () =>
      Object.keys(values)
        .filter((key) => key.toLowerCase().includes(filter.trim().toLowerCase()))
        .sort((left, right) => left.localeCompare(right)),
    [values, filter],
  );

  const versions = useQuery({
    queryKey: queryKeys.versions(project, environment, path),
    queryFn: () => listVersions(project, environment, path),
    enabled: showVersions,
  });

  function update(next: Record<string, string>) {
    setDraft(next);
    setError("");
    setNotice("");
  }

  async function reload() {
    setDraft(null);
    setDraftDescription(null);
    // One coarse invalidation per environment: a write can change the document,
    // its version history and the parent listing. Anything finer is guesswork, and
    // the old global ["entries"] key threw away every other environment too.
    await queryClient.invalidateQueries({ queryKey: queryKeys.env(project, environment) });
  }

  async function save() {
    const invalid = Object.keys(values).find((key) => !isValidKey(key));
    if (invalid) {
      setError(
        `"${invalid}" is not a valid key. Start with a letter or underscore, then letters, digits and underscores.`,
      );
      return;
    }
    if (Object.keys(values).length === 0) {
      setError("A secret needs at least one key. Use Delete secret to remove it entirely.");
      return;
    }

    setSaving(true);
    setError("");
    try {
      // The version doubles as a compare-and-set check: 0 creates, anything else
      // requires that nobody has written since this page loaded.
      await writeSecret(
        project,
        environment,
        path,
        values,
        currentVersion,
        description || undefined,
      );
      await reload();
      setNotice("Saved.");
    } catch (saveError) {
      // A protected environment refuses a direct write. The edit is not lost: it
      // becomes a change request once the author says why.
      if (needsApproval(saveError)) {
        setPendingReview({ values, description: description || undefined, deletion: false });
      } else {
        setError(errorMessage(saveError, "The secret could not be saved."));
      }
    } finally {
      setSaving(false);
    }
  }

  async function sendForReview(reason: string) {
    if (!pendingReview) return;
    await proposeChange(project, {
      environment,
      path,
      values: pendingReview.deletion ? {} : pendingReview.values,
      description: pendingReview.description,
      reason: reason || undefined,
      expectedVersion: currentVersion ?? undefined,
      delete: pendingReview.deletion,
    });
    setPendingReview(null);
    setNotice("Sent for review. It applies once somebody else approves it.");
  }

  async function removeDocument() {
    if (!window.confirm(`Delete every key at "${path}"? Previous versions stay recoverable.`)) {
      return;
    }
    try {
      await deleteSecret(project, environment, path);
      await reload();
      setNotice("Secret deleted. Older versions can still be restored.");
    } catch (deleteError) {
      if (needsApproval(deleteError)) {
        setPendingReview({ values: {}, description: undefined, deletion: true });
      } else {
        setError(errorMessage(deleteError, "The secret could not be deleted."));
      }
    }
  }

  async function purgeDocument() {
    // Deliberately harder than a delete: this erases every version, and OpenBao
    // cannot bring any of it back.
    const typed = window.prompt(
      `Destroying "${path}" erases every version permanently. Type the path to confirm.`,
    );
    if (typed !== path) return;
    try {
      await purgeSecret(project, environment, path);
      await reload();
      setNotice("Secret destroyed. Nothing is recoverable.");
    } catch (purgeError) {
      setError(errorMessage(purgeError, "The secret could not be destroyed."));
    }
  }

  async function download(format: "env" | "json") {
    setExportMenu(null);
    try {
      const contents = await exportSecrets(project, environment, path, format);
      const name = path.replaceAll("/", "-");
      downloadText(format === "env" ? `${name}.env` : `${name}.json`, contents);
    } catch (exportError) {
      setError(errorMessage(exportError, "The export failed."));
    }
  }

  if (secret.isLoading) return <LoadingRow label="Loading secret…" />;

  if (secret.isError) {
    return (
      <Alert severity="error">
        {errorMessage(secret.error, "The secret could not be loaded.")}
      </Alert>
    );
  }

  if (stored === null && draft === null) {
    return (
      <Paper sx={{ p: 4 }}>
        <Stack spacing={2} alignItems="flex-start">
          <Typography variant="h6">No secret at this path</Typography>
          <Typography color="text.secondary" variant="body2">
            {path} holds no values yet. Create one, or import an existing .env file.
          </Typography>
          <Stack direction="row" spacing={1}>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDraft({})}>
              Create secret here
            </Button>
            <Button startIcon={<UploadIcon />} onClick={() => setImporting(true)}>
              Import
            </Button>
          </Stack>
        </Stack>
        <ImportDialog
          open={importing}
          text={importText}
          onText={setImportText}
          onClose={() => {
            setImporting(false);
            setImportText("");
          }}
          onSubmit={async (parsed) => {
            await importSecrets(project, environment, path, parsed, 0);
            await reload();
            setNotice("Import complete.");
          }}
        />
      </Paper>
    );
  }

  return (
    <Paper sx={{ overflow: "hidden" }}>
      <Stack
        direction={{ xs: "column", md: "row" }}
        spacing={1}
        alignItems={{ md: "center" }}
        sx={{ p: 2, borderBottom: 1, borderColor: "divider" }}
      >
        <TextField
          placeholder="Filter keys"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          sx={{ flex: 1, minWidth: 200 }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />
        <Chip size="small" variant="outlined" label={`v${currentVersion}`} />
        <Button
          size="small"
          startIcon={revealAll ? <VisibilityOffIcon /> : <VisibilityIcon />}
          onClick={() => {
            setRevealAll(!revealAll);
            setRevealed({});
          }}
        >
          {revealAll ? "Hide all" : "Reveal all"}
        </Button>
        <Button size="small" startIcon={<UploadIcon />} onClick={() => setImporting(true)}>
          Import
        </Button>
        <Button
          size="small"
          startIcon={<DownloadIcon />}
          onClick={(event) => setExportMenu(event.currentTarget)}
        >
          Export
        </Button>
        <Menu anchorEl={exportMenu} open={Boolean(exportMenu)} onClose={() => setExportMenu(null)}>
          <MenuItem onClick={() => download("env")}>Download .env</MenuItem>
          <MenuItem onClick={() => download("json")}>Download JSON</MenuItem>
        </Menu>
        <Button
          size="small"
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => setAdding(true)}
        >
          Add key
        </Button>
      </Stack>

      {(error || notice) && (
        <Box sx={{ px: 2, pt: 2 }}>
          {error ? (
            <Alert severity="error" onClose={() => setError("")}>
              {error}
            </Alert>
          ) : (
            <Alert severity="success" onClose={() => setNotice("")}>
              {notice}
            </Alert>
          )}
        </Box>
      )}

      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell width="32%">Key</TableCell>
            <TableCell>Value</TableCell>
            <TableCell width={132} align="right">
              Actions
            </TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {keys.length === 0 && (
            <TableRow>
              <TableCell colSpan={3}>
                <EmptyState
                  title={filter ? "No keys match that filter" : "This secret has no keys yet"}
                  hint={filter ? undefined : "Use Add key to create the first one."}
                />
              </TableCell>
            </TableRow>
          )}
          {keys.map((key) => {
            const visible = revealAll || Boolean(revealed[key]);
            return (
              <TableRow key={key} hover>
                <TableCell sx={{ fontFamily: mono, fontSize: 13, wordBreak: "break-all" }}>
                  {key}
                </TableCell>
                <TableCell>
                  <TextField
                    value={values[key]}
                    type={visible ? "text" : "password"}
                    onChange={(event) => update({ ...values, [key]: event.target.value })}
                    fullWidth
                    autoComplete="off"
                    slotProps={{ htmlInput: { style: { fontFamily: mono, fontSize: 13 } } }}
                  />
                </TableCell>
                <TableCell align="right">
                  <Stack direction="row" justifyContent="flex-end">
                    <Tooltip title={visible ? "Hide" : "Reveal"}>
                      <IconButton
                        size="small"
                        onClick={() => setRevealed({ ...revealed, [key]: !visible })}
                      >
                        {visible ? (
                          <VisibilityOffIcon fontSize="small" />
                        ) : (
                          <VisibilityIcon fontSize="small" />
                        )}
                      </IconButton>
                    </Tooltip>
                    <CopyButton value={values[key]} title="Copy value" />
                    <Tooltip title="Remove key">
                      <IconButton
                        size="small"
                        color="error"
                        onClick={() => {
                          const next = { ...values };
                          delete next[key];
                          update(next);
                        }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </Stack>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>

      <Stack spacing={2} sx={{ p: 2, borderTop: 1, borderColor: "divider" }}>
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDraftDescription(event.target.value)}
          helperText="Stored as OpenBao custom metadata. Never put a secret value here."
          fullWidth
        />

        {/* Three tiers, not one row of seven. Saving is the thing you came to do;
            looking around is quieter; the two irreversible ones are behind a menu so
            "Destroy everything" cannot be clicked while aiming for "Version history". */}
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          {/* "Save values", not "Save changes": the Details section below has its own
              save, and a global-sounding label here made people think one covered both. */}
          <Button variant="contained" onClick={save} disabled={!dirty || saving}>
            {saving ? "Saving…" : dirty ? "Save values" : "Saved"}
          </Button>
          <Button
            onClick={() => {
              setDraft(null);
              setDraftDescription(null);
              setError("");
            }}
            disabled={!dirty || saving}
          >
            Discard
          </Button>

          <Box sx={{ flex: 1 }} />

          <Button
            size="small"
            color="inherit"
            startIcon={<HistoryIcon />}
            onClick={() => setShowVersions((value) => !value)}
          >
            {showVersions ? "Hide history" : "History"}
          </Button>
          <Button
            size="small"
            color="inherit"
            startIcon={<ShareIcon />}
            onClick={() => setSharing(true)}
            disabled={dirty}
          >
            Share once
          </Button>
          <Button
            size="small"
            color="inherit"
            component={Link}
            href={`/projects/${encodeURIComponent(project)}/compare/${path
              .split("/")
              .map(encodeURIComponent)
              .join("/")}`}
            startIcon={<CompareIcon />}
          >
            Compare
          </Button>
          <Tooltip title="More actions">
            <IconButton
              size="small"
              aria-label="More actions"
              onClick={(event) => setMoreMenu(event.currentTarget)}
            >
              <MoreIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Stack>

        <Menu
          anchorEl={moreMenu}
          open={moreMenu !== null}
          onClose={() => setMoreMenu(null)}
          anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
          transformOrigin={{ vertical: "top", horizontal: "right" }}
        >
          <MenuItem
            onClick={() => {
              setMoreMenu(null);
              void removeDocument();
            }}
          >
            <ListItemIcon>
              <DeleteIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary="Delete secret" secondary="You can bring it back." />
          </MenuItem>
          <MenuItem
            sx={{ color: "error.main" }}
            onClick={() => {
              setMoreMenu(null);
              void purgeDocument();
            }}
          >
            <ListItemIcon>
              <DeleteForeverIcon fontSize="small" color="error" />
            </ListItemIcon>
            <ListItemText primary="Destroy for good" secondary="Erased. Cannot be undone." />
          </MenuItem>
        </Menu>

        <SecretMetadataPanel project={project} environment={environment} path={path} />

        <Collapse in={showVersions} unmountOnExit>
          <VersionHistory
            versions={versions.data ?? []}
            loading={versions.isLoading}
            error={versions.error}
            currentVersion={currentVersion}
            onRestore={async (version) => {
              await restoreSecret(project, environment, path, version);
              await reload();
              setNotice(`Rolled back to version ${version}.`);
            }}
            onUndelete={async (version) => {
              await undeleteSecret(project, environment, path, version);
              await reload();
              setNotice(`Version ${version} is no longer deleted.`);
            }}
            onDestroy={async (version) => {
              await destroyVersions(project, environment, path, [version]);
              await reload();
              setNotice(`Version ${version} was destroyed and cannot be recovered.`);
            }}
            onError={setError}
          />
        </Collapse>
      </Stack>

      {dirty && (
        <Box
          sx={{
            position: "sticky",
            bottom: 0,
            px: 2,
            py: 1.5,
            bgcolor: "action.selected",
            borderTop: 1,
            borderColor: "divider",
          }}
        >
          <Typography variant="body2">
            Unsaved changes. They stay in this browser tab until you save.
          </Typography>
        </Box>
      )}

      <FormDialog
        open={pendingReview !== null}
        title={
          pendingReview?.deletion ? `Send this deletion for review` : `Send this change for review`
        }
        submitLabel="Send for review"
        onClose={() => {
          setPendingReview(null);
          setReviewReason("");
        }}
        onSubmit={async () => {
          await sendForReview(reviewReason);
          setReviewReason("");
        }}
      >
        <Alert severity="info">
          {environment} is protected, so this cannot be saved in one step. It will apply as soon as
          somebody other than you approves it. Your edit is kept until then.
        </Alert>
        <TextField
          label="Why is this change needed?"
          value={reviewReason}
          onChange={(event) => setReviewReason(event.target.value)}
          multiline
          minRows={2}
          autoFocus
          helperText="Optional, but it is what the reviewer reads first."
        />
      </FormDialog>

      <FormDialog
        open={adding}
        title="Add key"
        submitLabel="Add"
        disabled={!isValidKey(newKey)}
        onClose={() => {
          setAdding(false);
          setNewKey("");
          setNewValue("");
        }}
        onSubmit={() => {
          if (Object.prototype.hasOwnProperty.call(values, newKey)) {
            throw new Error(`${newKey} already exists in this secret.`);
          }
          update({ ...values, [newKey]: newValue });
        }}
      >
        <TextField
          label="Key"
          value={newKey}
          onChange={(event) => setNewKey(event.target.value)}
          error={newKey.length > 0 && !isValidKey(newKey)}
          helperText="Start with a letter or underscore, then letters, digits and underscores."
          autoFocus
          required
        />
        <TextField
          label="Value"
          value={newValue}
          onChange={(event) => setNewValue(event.target.value)}
          multiline
          minRows={2}
          slotProps={{ htmlInput: { style: { fontFamily: mono, fontSize: 13 } } }}
        />
        <Alert severity="info">The key joins your draft. Save to write it to OpenBao.</Alert>
      </FormDialog>

      <ShareLinkDialog open={sharing} onClose={() => setSharing(false)} values={values} />

      <ImportDialog
        open={importing}
        text={importText}
        onText={setImportText}
        onClose={() => {
          setImporting(false);
          setImportText("");
        }}
        onSubmit={async (parsed) => {
          await importSecrets(project, environment, path, { ...values, ...parsed }, currentVersion);
          await reload();
          setNotice(`Imported ${Object.keys(parsed).length} key(s).`);
        }}
      />
    </Paper>
  );
}

function ImportDialog({
  open,
  text,
  onText,
  onClose,
  onSubmit,
}: {
  open: boolean;
  text: string;
  onText: (value: string) => void;
  onClose: () => void;
  onSubmit: (values: Record<string, string>) => Promise<void>;
}) {
  const parsed = useMemo(() => {
    if (!text.trim()) return null;
    try {
      return parseSecretFile(text);
    } catch {
      return null;
    }
  }, [text]);

  const invalidKeys = Object.keys(parsed ?? {}).filter((key) => !isValidKey(key));
  const count = Object.keys(parsed ?? {}).length;

  return (
    <FormDialog
      open={open}
      title="Import secrets"
      submitLabel="Import"
      disabled={count === 0 || invalidKeys.length > 0}
      onClose={onClose}
      onSubmit={async () => {
        if (!parsed) throw new Error("There is nothing to import.");
        await onSubmit(parsed);
      }}
    >
      <Button component="label" startIcon={<UploadIcon />} variant="outlined">
        Choose a .env or .json file
        <input
          type="file"
          accept=".env,.json,text/plain,application/json"
          hidden
          onChange={async (event) => {
            const file = event.target.files?.[0];
            if (file) onText(await file.text());
            event.target.value = "";
          }}
        />
      </Button>
      <TextField
        label="Or paste .env / JSON"
        value={text}
        onChange={(event) => onText(event.target.value)}
        multiline
        minRows={6}
        slotProps={{ htmlInput: { style: { fontFamily: mono, fontSize: 13 } } }}
      />
      {text.trim() && !parsed && <Alert severity="error">That is not valid .env or JSON.</Alert>}
      {invalidKeys.length > 0 && (
        <Alert severity="error">
          These keys are not allowed: {invalidKeys.slice(0, 5).join(", ")}
          {invalidKeys.length > 5 ? "…" : ""}
        </Alert>
      )}
      {count > 0 && invalidKeys.length === 0 && (
        <Alert severity="info">
          {count} key(s) ready. Keys with the same name are replaced; the rest are kept.
        </Alert>
      )}
    </FormDialog>
  );
}

function VersionHistory({
  versions,
  loading,
  error,
  currentVersion,
  onRestore,
  onUndelete,
  onDestroy,
  onError,
}: {
  versions: { version: number | string; deletedAt: null | string; destroyed: boolean }[];
  loading: boolean;
  error: unknown;
  currentVersion: number;
  onRestore: (version: number) => Promise<void>;
  onUndelete: (version: number) => Promise<void>;
  onDestroy: (version: number) => Promise<void>;
  onError: (message: string) => void;
}) {
  const [busy, setBusy] = useState<number | null>(null);

  async function run(version: number, action: () => Promise<void>) {
    setBusy(version);
    try {
      await action();
    } catch (actionError) {
      onError(errorMessage(actionError, "That version action failed."));
    } finally {
      setBusy(null);
    }
  }

  if (loading) return <LoadingRow label="Loading version history…" />;
  if (error) {
    return <Alert severity="error">{errorMessage(error, "Version history is unavailable.")}</Alert>;
  }
  if (versions.length === 0) return <EmptyState title="No version history" />;

  const sorted = [...versions].sort((left, right) => Number(right.version) - Number(left.version));

  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell width={90}>Version</TableCell>
          <TableCell>Status</TableCell>
          <TableCell align="right">Actions</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {sorted.map((entry) => {
          const version = Number(entry.version);
          const isCurrent = version === currentVersion;
          return (
            <TableRow key={version} hover>
              <TableCell sx={{ fontFamily: mono }}>v{version}</TableCell>
              <TableCell>
                {entry.destroyed ? (
                  <Chip size="small" color="error" label="destroyed" />
                ) : entry.deletedAt ? (
                  <Chip
                    size="small"
                    color="warning"
                    label={`deleted ${new Date(entry.deletedAt).toLocaleString()}`}
                  />
                ) : isCurrent ? (
                  <Chip size="small" color="success" label="current" />
                ) : (
                  <Chip size="small" variant="outlined" label="available" />
                )}
              </TableCell>
              <TableCell align="right">
                <Stack direction="row" spacing={1} justifyContent="flex-end">
                  {entry.deletedAt && !entry.destroyed && (
                    <Button
                      size="small"
                      startIcon={<UndoIcon />}
                      disabled={busy === version}
                      onClick={() => run(version, () => onUndelete(version))}
                    >
                      Undelete
                    </Button>
                  )}
                  {!entry.destroyed && !isCurrent && (
                    <Button
                      size="small"
                      startIcon={<RestoreIcon />}
                      disabled={busy === version}
                      onClick={() => run(version, () => onRestore(version))}
                    >
                      Roll back
                    </Button>
                  )}
                  {!entry.destroyed && (
                    <Button
                      size="small"
                      color="error"
                      startIcon={<DeleteForeverIcon />}
                      disabled={busy === version}
                      onClick={() => {
                        // Destroy erases the data; delete only hides it. The wording
                        // has to make that difference impossible to miss.
                        if (
                          window.confirm(
                            `Destroy version ${version}? Its values are erased permanently — this is not the same as deleting.`,
                          )
                        ) {
                          void run(version, () => onDestroy(version));
                        }
                      }}
                    >
                      Destroy
                    </Button>
                  )}
                </Stack>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
