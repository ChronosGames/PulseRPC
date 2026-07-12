#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"

package_dir=$(mktemp -d)
cleanup() {
  rm -rf "$package_dir"
}
trap cleanup EXIT

dotnet pack ./PulseRPC.Packaging.slnf -c Release --warnaserror -o "$package_dir"

mapfile -t packages < <(find "$package_dir" -maxdepth 1 -name '*.nupkg' ! -name '*.snupkg' -print)
if [ "${#packages[@]}" -ne 9 ]; then
  echo "Expected 9 runtime packages, found ${#packages[@]}."
  exit 1
fi

for package in "${packages[@]}"; do
  unzip -Z1 "$package" | grep -Fxq 'README.md'
  readme=$(unzip -p "$package" README.md)
  grep -Fq 'samples/HelloRPC' <<<"$readme"
  if grep -Eq 'samples/(ChatApp|JwtAuthentication|HubFactoryExample|ServiceFactoryExample|JsonTranscoding)' <<<"$readme"; then
    echo "$(basename "$package") advertises a competing quickstart path."
    exit 1
  fi
done

echo "Verified ${#packages[@]} packaged NuGet README files."
