#!/bin/bash
# Follow-up to the 30-minute soak. First proves the order errors were only the clip wrapping, by checking every frame across
# loop restarts with the corrected check. Then repeats the load with no per-frame readback, to see the hand-off's own spikes.
cd "$(dirname "$0")"
pick() { grep -E 'FAILED|followed|streams=|order_errors|copy_ms|memory|RESULT' "$1" | cut -c1-165; }

echo '=== checked, 5 minutes, across loop restarts'
./vspike.exe --file fx/c1080.h264 --streams 4 --pace --strict --loops 30 --adapter nvidia > s1.txt 2>&1 &
A=$!
./vspike.exe --file fx/c2160.h264 --streams 1 --pace --strict --loops 50 --adapter nvidia --w 3840 --h 2160 > s2.txt 2>&1 &
B=$!
wait $A; wait $B
pick s1.txt; pick s2.txt

echo '=== unchecked, 5 minutes, no per-frame readback'
./vspike.exe --file fx/c1080.h264 --streams 4 --pace --loops 30 --adapter nvidia > s3.txt 2>&1 &
A=$!
./vspike.exe --file fx/c2160.h264 --streams 1 --pace --loops 50 --adapter nvidia --w 3840 --h 2160 > s4.txt 2>&1 &
B=$!
wait $A; wait $B
pick s3.txt; pick s4.txt
