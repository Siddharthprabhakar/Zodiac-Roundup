const CONTENT_TYPES = new Map([
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".wasm", "application/wasm"],
  [".unityweb", "application/octet-stream"],
  [".png", "image/png"],
  [".ico", "image/x-icon"]
]);

export default {
  async fetch(request, env, context) {
    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method not allowed", {
        status: 405,
        headers: { Allow: "GET, HEAD" }
      });
    }

    const url = new URL(request.url);
    let key;

    try {
      key = decodeURIComponent(url.pathname).replace(/^\/+/, "");
    } catch {
      return new Response("Invalid URL", { status: 400 });
    }

    if (key === "" || key.endsWith("/")) key += "index.html";
    if (key.includes("\0") || key.split("/").includes("..")) {
      return new Response("Invalid path", { status: 400 });
    }

    const cacheable = request.method === "GET" &&
      !request.headers.has("Range") &&
      key !== "index.html" &&
      !key.endsWith(".loader.js");

    if (cacheable) {
      const cached = await caches.default.match(request);
      if (cached) return cached;
    }

    const object = request.method === "HEAD"
      ? await env.GAME_BUCKET.head(key)
      : await env.GAME_BUCKET.get(key, { range: request.headers });

    if (object === null) {
      return new Response("Not found", {
        status: 404,
        headers: { "Content-Type": "text/plain; charset=utf-8" }
      });
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("ETag", object.httpEtag);
    headers.set("X-Content-Type-Options", "nosniff");
    headers.set("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.set("Content-Type", headers.get("Content-Type") || contentTypeFor(key));

    if (key === "index.html" || key.endsWith(".loader.js")) {
      headers.set("Cache-Control", "no-cache");
    } else {
      headers.set("Cache-Control", "public, max-age=86400");
    }

    let status = 200;
    if (request.headers.has("Range") && object.range) {
      const start = object.range.offset;
      const length = object.range.length;
      headers.set("Content-Range", `bytes ${start}-${start + length - 1}/${object.size}`);
      headers.set("Content-Length", String(length));
      status = 206;
    } else {
      headers.set("Content-Length", String(object.size));
    }

    const response = new Response(request.method === "HEAD" ? null : object.body, {
      status,
      headers
    });

    if (cacheable && status === 200) {
      context.waitUntil(caches.default.put(request, response.clone()));
    }

    return response;
  }
};

function contentTypeFor(key) {
  const lower = key.toLowerCase();
  for (const [extension, contentType] of CONTENT_TYPES) {
    if (lower.endsWith(extension)) return contentType;
  }
  return "application/octet-stream";
}
