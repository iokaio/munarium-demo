// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import java.nio.channels.*;
import java.nio.file.*;
import java.io.IOException;

public final class Lease implements AutoCloseable {
    private final FileChannel channel;
    private final FileLock lock;
    public Lease(Path directory) throws IOException {
        Files.createDirectories(directory); channel=FileChannel.open(directory.resolve(".lock"),StandardOpenOption.CREATE,StandardOpenOption.WRITE);
        try { lock=channel.tryLock(); if (lock==null) throw new IOException("Work item is busy"); }
        catch (IOException | RuntimeException error) { channel.close(); throw error; }
    }
    @Override public void close() throws IOException { try { lock.release(); } finally { channel.close(); } }
}
