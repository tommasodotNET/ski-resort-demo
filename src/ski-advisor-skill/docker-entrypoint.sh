#!/bin/sh
set -eu

# AddUvicornApp supplies only Uvicorn's arguments when it publishes this shared image.
if [ "$#" -gt 0 ] && [ "$1" = "skills_orchestrator_python.main:app" ]; then
    exec uvicorn "$@"
fi

exec "$@"
