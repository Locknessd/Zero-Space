"""Publish WebGL releases and retain the current and previous successful release."""
import json
import mimetypes
import os
import re
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import quote, urlparse


def metadata(path):
    name = path.name
    types = {".wasm.br": "application/wasm", ".js.br": "application/javascript",
             ".data.br": "application/octet-stream"}
    result = {"CacheControl": "public, max-age=31536000, immutable"}
    for suffix, content_type in types.items():
        if name.endswith(suffix):
            return {**result, "ContentType": content_type, "ContentEncoding": "br"}
    content_type = "application/javascript" if name.endswith(".js") else (
        mimetypes.guess_type(name)[0] or "application/octet-stream")
    return {**result, "ContentType": content_type}


def release_plan(directory, public_url, prefix, sha):
    directory = Path(directory)
    prefix = prefix.strip("/")
    if any(part in (".", "..", "") for part in prefix.split("/")) and prefix:
        raise ValueError("R2_PREFIX must be a folder path without empty, . or .. segments")
    if not re.fullmatch(r"[a-fA-F0-9]{40}", sha):
        raise ValueError("GITHUB_SHA must be a full commit SHA")
    parsed = urlparse(public_url)
    if parsed.scheme != "https" or not parsed.netloc or parsed.query or parsed.fragment:
        raise ValueError("R2_PUBLIC_URL must be an HTTPS bucket URL without query or fragment")
    root = "/".join(filter(None, [prefix, sha]))
    base = public_url.rstrip("/") + "/" + quote(root, safe="/")
    files = sorted(p for p in directory.rglob("*") if p.is_file())
    if not (directory / "index.html").is_file() or not (directory / "StreamingAssets").is_dir():
        raise ValueError("Incomplete build: index.html and StreamingAssets are required")
    config = {}
    for field, pattern in (("loaderUrl", "*.loader.js"), ("dataUrl", "*.data.br"),
                           ("frameworkUrl", "*.framework.js.br"), ("codeUrl", "*.wasm.br")):
        matches = list((directory / "Build").glob(pattern))
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one Build/{pattern}, found {len(matches)}")
        config[field] = base + "/" + quote(matches[0].relative_to(directory).as_posix(), safe="/")
    config["streamingAssetsUrl"] = base + "/StreamingAssets"
    manifest = {"commit": sha, "builtAt": datetime.now(timezone.utc).isoformat(),
                "baseUrl": base, "indexUrl": base + "/index.html", **config}
    uploads = [(p, root + "/" + p.relative_to(directory).as_posix(), metadata(p)) for p in files]
    return uploads, "/".join(filter(None, [prefix, "latest.json"])), manifest


def read_latest(client, bucket, key):
    try:
        data = client.get_object(Bucket=bucket, Key=key)
    except Exception as error:
        code = getattr(error, "response", {}).get("Error", {}).get("Code")
        if code in ("NoSuchKey", "404"):
            return {}
        raise
    result = json.loads(data["Body"].read())
    for field in ("commit", "previousCommit"):
        if result.get(field) and not re.fullmatch(r"[a-fA-F0-9]{40}", result[field]):
            raise ValueError(f"Invalid {field} in latest.json; refusing cleanup")
    if not result.get("commit"):
        raise ValueError("Missing commit in latest.json; refusing cleanup")
    return result


def object_keys(client, bucket, prefix):
    for page in client.get_paginator("list_objects_v2").paginate(Bucket=bucket, Prefix=prefix):
        for item in page.get("Contents", []):
            yield item["Key"]


def delete_release(client, bucket, parent, sha):
    if not re.fullmatch(r"[a-fA-F0-9]{40}", sha):
        raise ValueError("Refusing to delete a non-release path")
    # Trailing slash prevents prefix collisions and protects sibling files.
    marker = parent + sha + "/.release.json"
    keys = list(object_keys(client, bucket, parent + sha + "/"))
    # Keep the marker until all payload files are gone, so interrupted cleanup
    # remains discoverable on the next successful deployment.
    for key in sorted(keys, key=lambda key: key == marker):
        client.delete_object(Bucket=bucket, Key=key)


def cleanup(client, bucket, parent, latest):
    keep = {latest.get("commit"), latest.get("previousCommit")}
    # Only folders explicitly marked by this deploy script are managed.
    candidates = set()
    for key in object_keys(client, bucket, parent):
        relative = key[len(parent):]
        match = re.fullmatch(r"([a-fA-F0-9]{40})/\.release\.json", relative)
        if match and match[1] not in keep:
            candidates.add(match[1])
    for sha in sorted(candidates):
        delete_release(client, bucket, parent, sha)


def warn_cleanup(error):
    print(f"::warning::R2 cleanup did not finish ({type(error).__name__}); retry on next deploy.")


def deploy(client, bucket, uploads, manifest_key, manifest):
    parent = manifest_key.removesuffix("latest.json")
    previous = read_latest(client, bucket, manifest_key)
    sha = manifest["commit"]
    if sha == previous.get("commit"):
        # Reruns must not overwrite immutable files currently used by FE.
        manifest.clear()
        manifest.update(previous)
        try:
            cleanup(client, bucket, parent, previous)
        except Exception as error:
            warn_cleanup(error)
        return
    if sha == previous.get("previousCommit"):
        raise ValueError("This commit is retained for rollback; deploy a new commit instead")
    try:
        client.put_object(Bucket=bucket, Key=parent + sha + "/.release.json",
                          Body=json.dumps(manifest).encode(), ContentType="application/json",
                          CacheControl="no-store")
        for path, key, headers in uploads:
            client.upload_file(str(path), bucket, key, ExtraArgs=headers)
            actual = client.head_object(Bucket=bucket, Key=key)
            if actual["ContentLength"] != path.stat().st_size:
                raise RuntimeError(f"Uploaded size mismatch: {key}")
            for header, expected in headers.items():
                if actual.get(header) != expected:
                    raise RuntimeError(f"Uploaded metadata mismatch: {key}: {header}")
        if previous.get("commit"):
            manifest["previousCommit"] = previous["commit"]
        client.put_object(Bucket=bucket, Key=manifest_key,
                          Body=json.dumps(manifest, indent=2).encode(),
                          ContentType="application/json", CacheControl="no-store")
    except Exception:
        try:
            # A failed response may follow a successful manifest write. Read back
            # before deleting, and do nothing if the publication state is unknown.
            active = read_latest(client, bucket, manifest_key)
            if sha not in (active.get("commit"), active.get("previousCommit")):
                delete_release(client, bucket, parent, sha)
        except Exception as error:
            warn_cleanup(error)
        raise
    try:
        cleanup(client, bucket, parent, manifest)
    except Exception as error:
        # Publishing succeeded; leftover old files do not invalidate the release.
        warn_cleanup(error)


def main():
    import boto3
    from botocore.config import Config
    uploads, key, manifest = release_plan(
        "Build/WebGL-Desktop", os.environ["R2_PUBLIC_URL"],
        os.environ.get("R2_PREFIX", ""), os.environ["GITHUB_SHA"])
    client = boto3.client("s3", endpoint_url=os.environ["R2_ENDPOINT"],
                          aws_access_key_id=os.environ["R2_ACCESS_KEY_ID"],
                          aws_secret_access_key=os.environ["R2_SECRET_ACCESS_KEY"],
                          region_name="auto", config=Config(
                              request_checksum_calculation="when_required",
                              response_checksum_validation="when_required"))
    deploy(client, os.environ["R2_BUCKET"], uploads, key, manifest)
    url = os.environ["R2_PUBLIC_URL"].rstrip("/") + "/" + quote(key, safe="/")
    print(f"Published {len(uploads)} files. Manifest: {url}")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(f"### WebGL deployed\n\nManifest: {url}\n\nBuild: {manifest['indexUrl']}\n")


if __name__ == "__main__":
    main()
