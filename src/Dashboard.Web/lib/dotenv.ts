/** Keys the control plane accepts: a letter or underscore, then letters, digits, underscores. */
export const KEY_PATTERN = /^[A-Za-z_][A-Za-z0-9_]*$/;

export const isValidKey = (key: string) => KEY_PATTERN.test(key);

const ESCAPES: Record<string, string> = { n: "\n", r: "\r", '"': '"', "\\": "\\" };

/** Parses the common .env shapes, matching what the API's export writes. */
export function parseDotEnv(text: string): Record<string, string> {
  const values: Record<string, string> = {};

  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;

    const separator = trimmed.indexOf("=");
    if (separator < 1) continue;

    const key = trimmed
      .slice(0, separator)
      .trim()
      .replace(/^export\s+/, "");
    let value = trimmed.slice(separator + 1).trim();

    if (value.length > 1 && value.startsWith('"') && value.endsWith('"')) {
      // One pass, so an escaped backslash is not unescaped twice.
      value = value.slice(1, -1).replace(/\\([nr"\\])/g, (_, code: string) => ESCAPES[code]);
    } else if (value.length > 1 && value.startsWith("'") && value.endsWith("'")) {
      value = value.slice(1, -1);
    }

    values[key] = value;
  }

  return values;
}

/** Accepts either a .env file or a flat JSON object of string values. */
export function parseSecretFile(text: string): Record<string, string> {
  const trimmed = text.trim();
  // A leading bracket is JSON too. Without this it fell through to the .env parser,
  // which finds no assignments and returns an empty object — so a pasted array
  // silently imported nothing instead of reporting the problem.
  if (trimmed.startsWith("{") || trimmed.startsWith("[")) {
    const parsed: unknown = JSON.parse(trimmed);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
      throw new Error("JSON must be a flat object of key/value pairs.");
    }
    return Object.fromEntries(
      Object.entries(parsed as Record<string, unknown>).map(([key, value]) => [key, String(value)]),
    );
  }
  return parseDotEnv(trimmed);
}

export function downloadText(filename: string, contents: string) {
  const url = URL.createObjectURL(new Blob([contents], { type: "text/plain" }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}
