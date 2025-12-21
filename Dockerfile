# DBZ Legends Decompilation - Build Environment
FROM ubuntu:22.04

# Avoid interactive prompts
ENV DEBIAN_FRONTEND=noninteractive

# Install build tools
RUN apt-get update && apt-get install -y \
    build-essential \
    binutils-mips-linux-gnu \
    gcc-mips-linux-gnu \
    cpp-mips-linux-gnu \
    python3 \
    python3-pip \
    git \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
RUN pip3 install pyyaml

# Set working directory
WORKDIR /project

# Make cc1-psx executable
COPY bin/cc1-psx-26 /usr/local/bin/cc1-psx-26
RUN chmod +x /usr/local/bin/cc1-psx-26

# Default command
CMD ["/bin/bash"]
