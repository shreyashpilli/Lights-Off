cmake_minimum_required(VERSION 3.24)
project(RedLight LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "Red Light is a Windows desktop application.")
endif()

if(CMAKE_SIZEOF_VOID_P LESS 8)
    message(FATAL_ERROR "Build Red Light as x64. The Magnification API does not support WOW64.")
endif()

add_executable(RedLight WIN32
    src/main.cpp
)
add_executable(RedLightRecovery WIN32
    src/recovery.cpp
)

foreach(target RedLight RedLightRecovery)
    target_compile_features(${target} PRIVATE cxx_std_17)
    target_compile_definitions(${target} PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(${target} PRIVATE Magnification User32 Shell32 Comctl32 Advapi32 Dwmapi Ole32 UxTheme)
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8)
        target_link_options(${target} PRIVATE /SUBSYSTEM:WINDOWS)
    endif()
endforeach()

install(TARGETS RedLight RedLightRecovery RUNTIME DESTINATION .)
install(FILES README.md DESTINATION .)
