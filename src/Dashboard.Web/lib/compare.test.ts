import { describe, expect, it } from "vitest";
import { buildComparison } from "./compare";
import type { EnvironmentSnapshot } from "./client";

const env = (
  environment: string,
  values: Record<string, string> | null,
  accessible = true,
): EnvironmentSnapshot => ({
  environment,
  accessible,
  exists: values !== null,
  version: values === null ? 0 : 1,
  values: values ?? {},
});

describe("buildComparison", () => {
  it("marks a key identical when every environment agrees", () => {
    const { rows, identicalCount } = buildComparison([
      env("development", { A: "1" }),
      env("production", { A: "1" }),
    ]);
    expect(rows[0].status).toBe("identical");
    expect(identicalCount).toBe(1);
    // Same value, same group letter.
    expect(rows[0].cells.development).toMatchObject({ group: "A" });
    expect(rows[0].cells.production).toMatchObject({ group: "A" });
  });

  it("marks a key differing and groups the environments that agree", () => {
    const { rows } = buildComparison([
      env("development", { A: "1" }),
      env("staging", { A: "2" }),
      env("production", { A: "1" }),
    ]);
    expect(rows[0].status).toBe("differs");
    expect(rows[0].cells.development).toMatchObject({ group: "A" });
    expect(rows[0].cells.staging).toMatchObject({ group: "B" });
    expect(rows[0].cells.production).toMatchObject({ group: "A" });
  });

  it("distinguishes a missing key from a missing document", () => {
    const { rows } = buildComparison([
      env("development", { A: "1", B: "2" }),
      env("staging", { A: "1" }),
      env("production", null),
    ]);
    const b = rows.find((row) => row.key === "B")!;
    expect(b.status).toBe("partial");
    expect(b.cells.staging.kind).toBe("missing");
    expect(b.cells.production.kind).toBe("absent");
  });

  it("treats an empty value as its own state, not as missing", () => {
    const { rows } = buildComparison([env("development", { A: "" }), env("staging", { A: "x" })]);
    expect(rows[0].cells.development.kind).toBe("empty");
    expect(rows[0].status).toBe("differs");
  });

  it("excludes a locked environment from the status and reports it separately", () => {
    const { rows, compared, locked } = buildComparison([
      env("development", { A: "1" }),
      env("production", null, false),
    ]);
    expect(locked).toEqual(["production"]);
    expect(compared).toEqual(["development"]);
    expect(rows[0].cells.production.kind).toBe("locked");
    // Would be "partial" if the locked column counted as a gap.
    expect(rows[0].status).toBe("identical");
  });

  it("never surfaces a key from an environment it could not read", () => {
    const { rows } = buildComparison([
      env("development", { VISIBLE: "1" }),
      env("production", { HIDDEN: "1" }, false),
    ]);
    expect(rows.map((row) => row.key)).toEqual(["VISIBLE"]);
  });

  it("sorts keys so the table order is stable", () => {
    const { rows } = buildComparison([env("development", { B: "1", A: "1", C: "1" })]);
    expect(rows.map((row) => row.key)).toEqual(["A", "B", "C"]);
  });
});
