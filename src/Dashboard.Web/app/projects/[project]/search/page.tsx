"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableRow,
  TextField,
} from "@mui/material";
import SearchIcon from "@mui/icons-material/SearchOutlined";
import { EmptyState, LoadingRow, PageHeader } from "@/components/AppShell";
import EnvironmentChip from "@/components/EnvironmentChip";
import { errorMessage, searchSecrets } from "@/lib/client";
import { keys } from "@/lib/queryKeys";
import { mono } from "@/lib/theme";

export default function SearchPage() {
  const project = String(useParams<{ project: string }>().project);
  const [query, setQuery] = useState("");
  const trimmed = query.trim();

  const results = useQuery({
    queryKey: [...keys.project(project), "search", trimmed],
    queryFn: () => searchSecrets(project, trimmed),
    enabled: trimmed.length > 0,
  });

  return (
    <>
      <PageHeader
        title="Search"
        description={`Find a secret by path across every environment in ${project}. Environments you cannot read are skipped.`}
      />

      <TextField
        placeholder="Path contains…"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        autoFocus
        fullWidth
        sx={{ mb: 2, maxWidth: 480 }}
        slotProps={{
          input: {
            startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: "text.secondary" }} />,
          },
        }}
      />

      {results.isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage(results.error, "The search failed.")}
        </Alert>
      )}

      {results.data?.truncated && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Showing the first {results.data.hits.length} matches. Narrow the search to see the rest.
        </Alert>
      )}

      <Paper sx={{ overflow: "hidden" }}>
        {trimmed.length === 0 ? (
          <EmptyState title="Type to search" hint="Matches are made against the secret path." />
        ) : results.isLoading ? (
          <LoadingRow label="Scanning environments…" />
        ) : (results.data?.hits ?? []).length === 0 ? (
          <EmptyState title="No matches" hint={`Nothing in ${project} matches "${trimmed}".`} />
        ) : (
          <Table size="small">
            <TableBody>
              {results.data?.hits.map((hit) => (
                <TableRow key={`${hit.environment}:${hit.path}`} hover>
                  <TableCell width={140}>
                    <EnvironmentChip environment={hit.environment} />
                  </TableCell>
                  <TableCell>
                    <Box
                      component={Link}
                      href={`/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(hit.environment)}/secrets/${hit.path
                        .split("/")
                        .map(encodeURIComponent)
                        .join("/")}`}
                      sx={{
                        fontFamily: mono,
                        fontSize: 13,
                        display: "block",
                        "&:hover": { color: "primary.main" },
                      }}
                    >
                      {hit.path}
                    </Box>
                  </TableCell>
                  <TableCell align="right" width={120}>
                    <Box
                      component={Link}
                      href={`/projects/${encodeURIComponent(project)}/compare/${hit.path
                        .split("/")
                        .map(encodeURIComponent)
                        .join("/")}`}
                      sx={{ fontSize: 12, color: "primary.main" }}
                    >
                      Compare
                    </Box>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Paper>

      <Stack sx={{ mt: 2 }}>
        <Box sx={{ fontSize: 12, color: "text.disabled" }}>
          Searching matches the path only. Secret values are never scanned.
        </Box>
      </Stack>
    </>
  );
}
