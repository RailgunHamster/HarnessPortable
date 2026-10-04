#!/bin/sh
# Read-only SSH helper. Arguments: unit, user|system, remote port, optional log.
# stdout contains only a verified URL. 3=unavailable, 4=rejected, 5=invalid,
# 6=service restarted while fetching. Never starts services or invokes sudo.
set -f
export LC_ALL=C
unit=${1:-dsh-web.service}
scope=${2:-user}
expected_port=${3:-3080}
log_path=${4:-}
case "$unit" in -*|*/*|*[[:space:]]*) exit 5 ;; esac
case "$unit" in *.service) ;; *) exit 5 ;; esac
case "$expected_port" in ''|*[!0-9]*) exit 5 ;; esac
case "$scope" in
  user)
    export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
    set -- --user
    ;;
  system) set -- ;;
  *) exit 5 ;;
esac
command -v systemctl >/dev/null 2>&1 || exit 3
command -v curl >/dev/null 2>&1 || exit 3
properties=$(systemctl "$@" show --property=ActiveState --property=InvocationID -- "$unit" 2>/dev/null) || exit 3
state=$(printf '%s\n' "$properties" | sed -n 's/^ActiveState=//p')
invocation=$(printf '%s\n' "$properties" | sed -n 's/^InvocationID=//p')
[ "$state" = active ] && [ -n "$invocation" ] || exit 3
read_log() {
  if [ -n "$log_path" ]; then
    cat -- "$log_path" 2>/dev/null
  else
    journalctl "$@" --unit="$unit" --no-pager --output=cat "_SYSTEMD_INVOCATION_ID=$invocation" 2>/dev/null
  fi
}
u=$(read_log "$@" | sed -nE 's/^.*dsh web: (https?:\/\/[^[:space:]]+).*$/\1/p' | tail -n 1)
[ -n "$u" ] || exit 3
case "$u" in *[\?\&]token=?*) ;; *) exit 3 ;; esac
authority=${u#*://}
authority=${authority%%/*}
authority=${authority%%\?*}
[ "${authority##*:}" = "$expected_port" ] || exit 5
code=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 3 --noproxy '*' -- "$u") || exit 4
[ "$code" = 303 ] || exit 4
current=$(systemctl "$@" show --property=InvocationID --value -- "$unit" 2>/dev/null) || exit 3
[ "$current" = "$invocation" ] || exit 6
printf '%s\n' "$u"
