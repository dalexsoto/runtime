project(SwiftValueLifecycle)
include ("${CLR_INTEROP_TEST_ROOT}/Interop.cmake")

set(SOURCE SwiftValueLifecycle)

if (NOT SWIFT_COMPILER_TARGET AND CLR_CMAKE_TARGET_APPLE)
    set(SWIFT_PLATFORM "macosx")
    set(SWIFT_PLATFORM_SUFFIX "")
    if (NOT CMAKE_OSX_DEPLOYMENT_TARGET)
        set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0")
    endif()
    set(SWIFT_DEPLOYMENT_TARGET ${CMAKE_OSX_DEPLOYMENT_TARGET})
    set(SWIFT_COMPILER_TARGET "${CMAKE_OSX_ARCHITECTURES}-apple-${SWIFT_PLATFORM}${SWIFT_DEPLOYMENT_TARGET}${SWIFT_PLATFORM_SUFFIX}")
endif()

add_custom_target(${SOURCE} ALL
    COMMAND xcrun swiftc -target ${SWIFT_COMPILER_TARGET} -enable-library-evolution -emit-library ${CMAKE_CURRENT_SOURCE_DIR}/${SOURCE}.swift -o ${CMAKE_CURRENT_BINARY_DIR}/lib${SOURCE}.dylib
    COMMAND xcrun clang -target ${SWIFT_COMPILER_TARGET} -O1 -dynamiclib ${CMAKE_CURRENT_SOURCE_DIR}/SwiftPtrauthHelper.c -install_name @rpath/libSwiftPtrauthHelper.dylib -o ${CMAKE_CURRENT_BINARY_DIR}/libSwiftPtrauthHelper.dylib
    DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/${SOURCE}.swift ${CMAKE_CURRENT_SOURCE_DIR}/SwiftPtrauthHelper.c
    COMMENT "Generating ${SOURCE} library"
)

install(FILES ${CMAKE_CURRENT_BINARY_DIR}/lib${SOURCE}.dylib ${CMAKE_CURRENT_BINARY_DIR}/libSwiftPtrauthHelper.dylib
    DESTINATION bin
)
