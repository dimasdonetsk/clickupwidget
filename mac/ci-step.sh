#!/bin/bash
# Обгортка для кроків CI: виконує команду, а якщо вона впала — виносить останні рядки виводу
# в анотацію GitHub (їх видно на сторінці збірки й через API навіть без доступу до повних журналів).
# З CI_STEP_NOTICE=1 короткий вивід показується й тоді, коли все гаразд.
#   bash mac/ci-step.sh "Назва" команда аргументи…
TITLE="$1"; shift
LOG=$(mktemp)
set +e
"$@" 2>&1 | tee "$LOG"
CODE=${PIPESTATUS[0]}
encode() { tail -n "$1" "$LOG" | sed -e 's/%/%25/g' -e 's/\r/%0D/g' | awk '{printf "%s%%0A", $0}'; }
if [ "$CODE" -ne 0 ]; then
  echo "::error title=$TITLE (код $CODE)::$(encode 60)"
elif [ "${CI_STEP_NOTICE:-0}" = "1" ]; then
  echo "::notice title=$TITLE::$(encode 40)"
fi
rm -f "$LOG"
exit "$CODE"
