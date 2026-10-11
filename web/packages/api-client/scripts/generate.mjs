import { readFile, writeFile } from "node:fs/promises";
import openapiTS, { astToString } from "openapi-typescript";

const source = new URL("../openapi.json", import.meta.url);
const document = JSON.parse(await readFile(source, "utf8"));

// The .NET generator describes an integer as "integer or string"; the client only ever reads numbers.
function tightenNumbers(node) {
  if (Array.isArray(node)) {
    node.forEach(tightenNumbers);
    return;
  }
  if (node && typeof node === "object") {
    if (Array.isArray(node.type) && node.type.includes("string") && (node.type.includes("integer") || node.type.includes("number"))) {
      node.type = node.type.filter((t) => t !== "string");
      if (node.type.length === 1) node.type = node.type[0];
      delete node.pattern;
    }
    for (const value of Object.values(node)) tightenNumbers(value);
  }
}

// The .NET generator lists no property as required; one that admits no null is always written.
function markRequired(node) {
  if (Array.isArray(node)) {
    node.forEach(markRequired);
    return;
  }
  if (node && typeof node === "object") {
    if (node.type === "object" && node.properties && !node.required) {
      const required = Object.entries(node.properties)
        // A $ref carries no null marker even when the .NET property is nullable.
        .filter(([, schema]) => schema.type !== undefined && !(Array.isArray(schema.type) && schema.type.includes("null")) && schema.nullable !== true)
        .map(([name]) => name);
      if (required.length) node.required = required;
    }
    for (const value of Object.values(node)) markRequired(value);
  }
}

tightenNumbers(document);
markRequired(document);

const ast = await openapiTS(document, { alphabetize: true });
const banner = "// Generated from openapi.json by scripts/generate.mjs. Do not edit; regenerate.\n";
await writeFile(new URL("../src/schema.d.ts", import.meta.url), banner + astToString(ast));
console.log("wrote src/schema.d.ts");
