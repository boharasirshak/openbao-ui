import { describe, expect, it } from "vitest";
import { isValidKey, parseDotEnv, parseSecretFile } from "./dotenv";

describe("parseDotEnv", () => {
  it("reads plain assignments and skips comments and blank lines", () => {
    expect(
      parseDotEnv(`
        # a comment
        A=1

        B=two
      `),
    ).toEqual({ A: "1", B: "two" });
  });

  it("strips the export prefix", () => {
    expect(parseDotEnv("export API_KEY=abc")).toEqual({ API_KEY: "abc" });
  });

  it("keeps '=' inside a value", () => {
    expect(parseDotEnv("URL=postgres://u:p@h/db?x=1")).toEqual({
      URL: "postgres://u:p@h/db?x=1",
    });
  });

  it("unescapes a double-quoted value in one pass", () => {
    // Two passes would turn \\n into a newline. The escaped backslash must survive
    // and the \n beside it must not.
    expect(parseDotEnv(String.raw`A="back\\slash and \n newline and \"quote\""`)).toEqual({
      A: 'back\\slash and \n newline and "quote"',
    });
  });

  it("takes a single-quoted value literally", () => {
    expect(parseDotEnv(String.raw`A='no \n escapes'`)).toEqual({ A: String.raw`no \n escapes` });
  });

  it("ignores lines with no key", () => {
    expect(parseDotEnv("=orphan\nnot-an-assignment\nA=1")).toEqual({ A: "1" });
  });
});

describe("parseSecretFile", () => {
  it("accepts a flat JSON object and coerces values to strings", () => {
    expect(parseSecretFile('{"A":"1","B":2,"C":true}')).toEqual({ A: "1", B: "2", C: "true" });
  });

  it("rejects JSON that is not a flat object", () => {
    expect(() => parseSecretFile("[1,2]")).toThrow();
  });

  it("falls back to .env for anything that does not start with a brace", () => {
    expect(parseSecretFile("A=1")).toEqual({ A: "1" });
  });
});

describe("isValidKey", () => {
  it.each([
    ["DATABASE_URL", true],
    ["_PRIVATE", true],
    ["a1", true],
    ["1LEADING_DIGIT", false],
    ["HAS-DASH", false],
    ["has space", false],
    ["", false],
  ])("%s -> %s", (key, expected) => {
    expect(isValidKey(key)).toBe(expected);
  });
});
