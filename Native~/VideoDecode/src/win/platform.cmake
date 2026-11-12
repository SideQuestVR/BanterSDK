# Windows: Media Foundation decoders, the D3D11 video processor and a keyed-mutex ring shared with Unity's device
# (src/win), included from the top CMakeLists.txt.
#
# Media Foundation is late-bound (LoadLibraryExW + GetProcAddress) so the DLL still loads on Windows N editions; mfuuid is
# GUID data only. Direct3D 11 and DXGI are part of every Windows the player runs on.
# flip.hlsl is compiled by the Windows SDK's fxc into generated/flip_vs.h and flip_ps.h (the flip pass for video
# processors that can't mirror).

file(GLOB BSVD_WIN_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_LIST_DIR}/*.cpp")
list(APPEND BSVD_PLATFORM_SOURCES ${BSVD_WIN_SOURCES})
list(APPEND BSVD_PLATFORM_LIBRARIES d3d11 dxgi ole32 mfuuid)

file(GLOB BSVD_WIN_TOOL_SOURCES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/tools/bsvdtool/win*.cpp")
list(APPEND BSVD_TOOL_SOURCES ${BSVD_WIN_TOOL_SOURCES})
list(APPEND BSVD_TOOL_LIBRARIES winmm)  # timeBeginPeriod: the stand-in render thread ticks on time

# The SDK CMake builds against first, then any.
set(BSVD_FXC_HINTS "")
get_filename_component(BSVD_KITS_ROOT "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows Kits\\Installed Roots;KitsRoot10]" ABSOLUTE)
foreach(version ${CMAKE_VS_WINDOWS_TARGET_PLATFORM_VERSION} ${CMAKE_SYSTEM_VERSION})
    list(APPEND BSVD_FXC_HINTS "${BSVD_KITS_ROOT}/bin/${version}/x64")
endforeach()
file(GLOB BSVD_FXC_ANY LIST_DIRECTORIES true "${BSVD_KITS_ROOT}/bin/10.*/x64")
list(SORT BSVD_FXC_ANY ORDER DESCENDING)
list(APPEND BSVD_FXC_HINTS ${BSVD_FXC_ANY})
find_program(BSVD_FXC fxc HINTS ${BSVD_FXC_HINTS} NO_DEFAULT_PATH)
find_program(BSVD_FXC fxc)
if(NOT BSVD_FXC)
    message(FATAL_ERROR "fxc.exe not found: install the Windows 10/11 SDK (Visual Studio Build Tools component).")
endif()

set(BSVD_WIN_GENERATED "${CMAKE_CURRENT_BINARY_DIR}/generated")
add_custom_command(
    OUTPUT "${BSVD_WIN_GENERATED}/flip_vs.h" "${BSVD_WIN_GENERATED}/flip_ps.h"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${BSVD_WIN_GENERATED}"
    COMMAND "${BSVD_FXC}" /nologo /O3 /T vs_4_0 /E FlipVs /Vn kFlipVs /Fh "${BSVD_WIN_GENERATED}/flip_vs.h" "${CMAKE_CURRENT_LIST_DIR}/flip.hlsl"
    COMMAND "${BSVD_FXC}" /nologo /O3 /T ps_4_0 /E FlipPs /Vn kFlipPs /Fh "${BSVD_WIN_GENERATED}/flip_ps.h" "${CMAKE_CURRENT_LIST_DIR}/flip.hlsl"
    DEPENDS "${CMAKE_CURRENT_LIST_DIR}/flip.hlsl"
    COMMENT "fxc flip.hlsl"
    VERBATIM)
add_custom_target(bsvd_win_shaders DEPENDS "${BSVD_WIN_GENERATED}/flip_vs.h" "${BSVD_WIN_GENERATED}/flip_ps.h")
add_dependencies(bsvd_platform_generated bsvd_win_shaders)
target_include_directories(bsvd_common INTERFACE "${BSVD_WIN_GENERATED}")
