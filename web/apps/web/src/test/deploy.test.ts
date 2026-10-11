// @vitest-environment node
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { expect, it } from "vitest";

const read = (path: string) => readFileSync(fileURLToPath(new URL(path, import.meta.url)), "utf8");

it("renders every runtime value the identity helper reads", () => {
  const read_by_auth = [...read("../lib/auth.ts").matchAll(/runtime\.(\w+)/g)].map((m) => m[1]!);
  const template = read("../../deploy/config.js.template");
  const rendered = [...template.matchAll(/(\w+): "\$\{(\w+)\}"/g)].map((m) => ({ key: m[1]!, variable: m[2]! }));
  const exported = /envsubst '([^']+)'/.exec(read("../../deploy/10-render-config.sh"))?.[1] ?? "";

  expect(new Set(rendered.map((r) => r.key))).toEqual(new Set(read_by_auth));
  for (const { variable } of rendered) expect(exported).toContain(`\${${variable}}`);
});
