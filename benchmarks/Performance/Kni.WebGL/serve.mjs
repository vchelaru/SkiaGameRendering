// Serves a published browser app (the wwwroot folder of `dotnet publish`) on a local port.
//   node serve.mjs Raw/bin/Release/net8.0/publish/wwwroot 5101
import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve } from "node:path";

const root = resolve(process.argv[2]);
const port = Number(process.argv[3] ?? 5101);
const types = {
  ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".css": "text/css", ".json": "application/json",
  ".wasm": "application/wasm", ".dat": "application/octet-stream", ".dll": "application/octet-stream", ".pdb": "application/octet-stream",
  ".png": "image/png", ".ico": "image/x-icon",
};

createServer((request, response) => {
  const path = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
  let file = normalize(join(root, path === "/" ? "index.html" : path));
  if (!file.startsWith(root)) { response.writeHead(403).end(); return; }
  if (!existsSync(file) || statSync(file).isDirectory()) file = join(root, "index.html");
  response.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream", "Cache-Control": "no-store" });
  createReadStream(file).pipe(response);
}).listen(port, "127.0.0.1", () => console.log(`Serving ${root} on http://127.0.0.1:${port}/`));
