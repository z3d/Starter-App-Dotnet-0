import { writeFile } from "node:fs/promises";

const url = process.env.OPENAPI_URL ?? "http://localhost:5164/openapi/v1.json";
const response = await fetch(url);
if (!response.ok) throw new Error(`${url} answered ${response.status}; is the API running in Development?`);
const document = await response.json();
// The servers entry names whichever host the API was running on.
delete document.servers;
await writeFile(new URL("../openapi.json", import.meta.url), JSON.stringify(document, null, 2) + "\n");
console.log(`wrote openapi.json from ${url}`);
await import("./generate.mjs");
