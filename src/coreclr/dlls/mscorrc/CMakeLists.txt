include_directories("../../pal/prebuilt/corerror")

if(CLR_CMAKE_HOST_WIN32)
    add_library_clr(mscorrc OBJECT
        include.rc
    )

else()
    build_resources(${CMAKE_CURRENT_SOURCE_DIR}/include.rc mscorrc TARGET_CPP_FILE)
    add_custom_command(
        OUTPUT ${CMAKE_CURRENT_BINARY_DIR}/mscorrc.rc.i
        APPEND
        DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/mscorrc.rc ${CMAKE_CURRENT_SOURCE_DIR}/resource.h
    )
    add_library_clr(mscorrc OBJECT ${TARGET_CPP_FILE})

endif(CLR_CMAKE_HOST_WIN32)
