import type { EnvironmentSnapshot } from "./client";

/**
 * One environment's answer for one key.
 *  value    — present and readable
 *  empty    — present but set to ""
 *  missing  — the document exists here, this key does not
 *  absent   — there is no document at this path in this environment
 *  locked   — the caller cannot read this environment
 */
export type Cell =
  | { kind: "value"; value: string; group: string }
  | { kind: "empty"; group: string }
  | { kind: "missing" }
  | { kind: "absent" }
  | { kind: "locked" };

export type RowStatus = "identical" | "differs" | "partial";

export type Row = {
  key: string;
  status: RowStatus;
  cells: Record<string, Cell>;
};

export type Comparison = {
  rows: Row[];
  compared: string[];
  locked: string[];
  identicalCount: number;
};

const GROUP_LABELS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

/**
 * Values are compared as exact strings, never hashed. Every value is already in
 * memory here, so hashing would add a collision risk that could render two different
 * secrets as identical — on the one screen where that mistake is most expensive.
 */
export function buildComparison(snapshots: EnvironmentSnapshot[]): Comparison {
  const locked = snapshots.filter((s) => !s.accessible).map((s) => s.environment);
  const readable = snapshots.filter((s) => s.accessible);
  const compared = readable.map((s) => s.environment);

  const keys = [...new Set(readable.flatMap((s) => Object.keys(s.values)))].sort((a, b) =>
    a.localeCompare(b),
  );

  const rows = keys.map<Row>((key) => {
    // Group letters say which environments agree without revealing the value itself.
    const groups = new Map<string, string>();
    const cells: Record<string, Cell> = {};

    for (const snapshot of snapshots) {
      if (!snapshot.accessible) {
        cells[snapshot.environment] = { kind: "locked" };
        continue;
      }
      if (!snapshot.exists) {
        cells[snapshot.environment] = { kind: "absent" };
        continue;
      }
      if (!(key in snapshot.values)) {
        cells[snapshot.environment] = { kind: "missing" };
        continue;
      }

      const value = snapshot.values[key];
      let group = groups.get(value);
      if (group === undefined) {
        group = GROUP_LABELS[groups.size] ?? "?";
        groups.set(value, group);
      }
      cells[snapshot.environment] =
        value === "" ? { kind: "empty", group } : { kind: "value", value, group };
    }

    // Only readable environments decide the status; a locked one is excluded rather
    // than counted as a difference.
    const present = compared.map((environment) => cells[environment]);
    const anyGap = present.some((cell) => cell.kind === "missing" || cell.kind === "absent");
    const status: RowStatus = anyGap ? "partial" : groups.size > 1 ? "differs" : "identical";

    return { key, status, cells };
  });

  return {
    rows,
    compared,
    locked,
    identicalCount: rows.filter((row) => row.status === "identical").length,
  };
}
