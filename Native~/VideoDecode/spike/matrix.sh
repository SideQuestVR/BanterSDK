#!/bin/bash
# V1 Windows matrix. Prints one compact block per case.
cd "$(dirname "$0")"
run() {
  name="$1"; shift
  printf '\n### %s\n' "$name"
  timeout 120 ./vspike.exe "$@" 2>&1 | grep -E 'FAILED|decoder wants|not a GPU|no decoder|not D3D11|followed|streams=|order_errors|worst_bar|RESULT' | cut -c1-140
  [ "${PIPESTATUS[0]}" = "124" ] && echo "TIMED OUT"
}

for gpu in nvidia intel; do
  run "H.264 High, B-frames, $gpu"        --file fx/c1080.h264 --strict --adapter $gpu
  run "H.264 Baseline, $gpu"              --file fx/c1080base.h264 --strict --adapter $gpu
  run "HEVC, $gpu"                        --file fx/c1080.hevc --strict --adapter $gpu
  run "H.264 4K, $gpu"                    --file fx/c2160.h264 --strict --adapter $gpu --w 3840 --h 2160
  run "bars 709 limited, $gpu"            --file fx/bars709lim.h264  --bars --matrix 709 --adapter $gpu
  run "bars 709 full, $gpu"               --file fx/bars709full.h264 --bars --matrix 709 --full --adapter $gpu
  run "bars 601 limited, $gpu"            --file fx/bars601lim.h264  --bars --matrix 601 --adapter $gpu
  run "bars 601 full, $gpu"               --file fx/bars601full.h264 --bars --matrix 601 --full --adapter $gpu
done

run "cross-GPU: decode on NVIDIA, consume on Intel" --file fx/c1080.h264 --adapter nvidia --cross --consumer intel
run "cross-GPU: decode on Intel, consume on NVIDIA" --file fx/c1080.h264 --adapter intel --cross --consumer nvidia

for n in 1 4 8 16 32; do
  run "paced 1080p30 x$n, one device, NVIDIA" --file fx/c1080.h264 --streams $n --pace --adapter nvidia
done
for n in 1 2 4 8; do
  run "paced 4K30 x$n, one device, NVIDIA" --file fx/c2160.h264 --streams $n --pace --adapter nvidia --w 3840 --h 2160
done
run "paced 1080p30 x8, private devices" --file fx/c1080.h264 --streams 8 --pace --shared 0 --adapter nvidia
run "paced 1080p30 x4, Intel" --file fx/c1080.h264 --streams 4 --pace --adapter intel
run "paced 1080p30 x8, Intel" --file fx/c1080.h264 --streams 8 --pace --adapter intel
run "flat out 1080p x1, NVIDIA" --file fx/c1080.h264 --loops 6 --adapter nvidia
run "flat out 1080p x4, NVIDIA" --file fx/c1080.h264 --loops 4 --streams 4 --adapter nvidia
