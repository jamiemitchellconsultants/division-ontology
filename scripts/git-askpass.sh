#!/bin/sh
case "$1" in
  *Username*) printf '%s\n' 'x-access-token' ;;
  *Password*) printf '%s\n' "${GH_SOURCE_TOKEN:?GH_SOURCE_TOKEN is required}" ;;
  *) exit 1 ;;
esac
