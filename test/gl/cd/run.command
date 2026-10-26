#!/bin/sh
# Builds and runs the GL smoke test in /tmp; the bundle's call log goes to /tmp/g.log.
cd /tmp && cp /Volumes/GLTEST/gltest.c . && gcc -o gltest gltest.c -framework GLUT -framework OpenGL || exit 1
RDN_GLD_LOG=/tmp/g.log ./gltest 2>&1 | grep -v CFGetHost
grep attach /tmp/g.log | tail -2 | cut -c 1-100
