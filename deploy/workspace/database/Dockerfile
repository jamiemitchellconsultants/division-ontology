# syntax=docker/dockerfile:1
ARG POSTGRES_IMAGE=postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea
FROM ${POSTGRES_IMAGE} AS build
RUN apk add --no-cache build-base bison flex curl perl
WORKDIR /build
RUN set -eux; \
    curl -fSL --retry 3 https://archive.apache.org/dist/age/PG16/1.6.0/apache-age-1.6.0-src.tar.gz -o age.tar.gz; \
    echo '1e0a434d53d669e5d067721e1eba8c3c3c2ae072709d5f90783beeba6f5e88f592fa5d8808d621b720c22edfe8c698baa9b0adb9c7b4e3e2a83e7a753bd795c8  age.tar.gz' | sha512sum -c -; \
    mkdir age; tar -xzf age.tar.gz -C age --strip-components=1; \
    make -C age -j"$(getconf _NPROCESSORS_ONLN)" with_llvm=no; \
    make -C age with_llvm=no DESTDIR=/extensions install
RUN set -eux; \
    curl -fSL --retry 3 https://codeload.github.com/pgvector/pgvector/tar.gz/refs/tags/v0.8.7 -o pgvector.tar.gz; \
    echo 'cac0b10c360f05b2d521200105ba3697e773d4cd3731f5a915a7e37ebe0bea85  pgvector.tar.gz' | sha256sum -c -; \
    mkdir pgvector; tar -xzf pgvector.tar.gz -C pgvector --strip-components=1; \
    make -C pgvector -j"$(getconf _NPROCESSORS_ONLN)" with_llvm=no OPTFLAGS=''; \
    make -C pgvector with_llvm=no DESTDIR=/extensions install

FROM ${POSTGRES_IMAGE}
COPY --from=build /extensions/ /
CMD ["postgres", "-c", "session_preload_libraries=age"]
