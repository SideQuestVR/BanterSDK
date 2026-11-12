#!/bin/bash
# The plan's proposed bar on Windows: four 1080p30 streams and one 4K30 at once for 30 minutes, every frame checked.
cd "$(dirname "$0")"
date +%H:%M:%S > soak.start
./vspike.exe --file fx/c1080.h264 --streams 4 --pace --strict --loops 180 --adapter nvidia > soak1080.txt 2>&1 &
A=$!
./vspike.exe --file fx/c2160.h264 --streams 1 --pace --strict --loops 300 --adapter nvidia --w 3840 --h 2160 > soak2160.txt 2>&1 &
B=$!
wait $A; EA=$?
wait $B; EB=$?
date +%H:%M:%S > soak.end
echo "exit 1080=$EA 2160=$EB"
grep -E 'FAILED|followed|streams=|order_errors|copy_ms|memory|RESULT' soak1080.txt | cut -c1-160
grep -E 'FAILED|followed|streams=|order_errors|copy_ms|memory|RESULT' soak2160.txt | cut -c1-160
