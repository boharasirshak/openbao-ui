import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import prettier from "eslint-config-prettier";

export default tseslint.config(
  { ignores: [".next/**", "dist/**", "node_modules/**", "lib/generated.ts", "next-env.d.ts"] },
  js.configs.recommended,
  ...tseslint.configs.recommendedTypeChecked,
  {
    languageOptions: {
      parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname },
    },
    plugins: { "react-hooks": reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,

      // A secret must never reach a log or a crash report.
      "no-console": ["error", { allow: ["warn", "error"] }],

      // A promise dropped in ordinary code is a silently swallowed failure, so this
      // stays on. Async JSX event handlers are excluded: React supports them, every
      // handler here already reports its own errors, and the alternative is wrapping
      // a dozen call sites in `void` without gaining any safety.
      "@typescript-eslint/no-floating-promises": "error",
      "@typescript-eslint/no-misused-promises": [
        "error",
        { checksVoidReturn: { attributes: false } },
      ],
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_" }],

      "no-restricted-globals": [
        "error",
        {
          name: "confirm",
          message: "Use the confirm dialog so destructive actions can be typed-confirmed.",
        },
        { name: "alert", message: "Use the toast provider." },
      ],
      "no-restricted-syntax": [
        "error",
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: "Not in a secrets UI.",
        },
      ],
    },
  },
  {
    // Config and test files run in Node and are not part of the app's type project.
    // The rules key must merge with disableTypeChecked's, not replace it.
    files: ["**/*.config.{mjs,ts}", "**/*.test.{ts,tsx}", "test/**"],
    ...tseslint.configs.disableTypeChecked,
    rules: { ...tseslint.configs.disableTypeChecked.rules, "no-console": "off" },
  },
  prettier,
);
