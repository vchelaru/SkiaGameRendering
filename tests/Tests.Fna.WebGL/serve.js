// Static server for the published Sample.Fna.WebGL wwwroot. `node serve.js <dir> <port>`.
const http = require("http");
const fs = require("fs");
const path = require("path");

const [root, port] = [path.resolve(process.argv[2]), Number(process.argv[3])];
const types = {
  ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
  ".wasm": "application/wasm", ".dat": "application/octet-stream", ".pdb": "application/octet-stream",
};

http.createServer((request, response) => {
  const url = decodeURIComponent(new URL(request.url, "http://x").pathname);
  const file = path.join(root, url === "/" ? "index.html" : url);
  if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, { "Content-Type": types[path.extname(file)] ?? "application/octet-stream", "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(response);
}).listen(port, "127.0.0.1");
