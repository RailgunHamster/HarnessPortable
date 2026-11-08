package com.harness.portable

import org.junit.Assert.*
import org.junit.Test

class ConnectionOrderingTests {
    private val keys = listOf("tunnel:a", "direct:https://fixture.invalid", "tunnel:b")

    @Test fun savedOrderSpansBothKindsAndAppendsNewEntriesWithoutDeletedOrDuplicateKeys() {
        assertEquals(listOf(keys[1], keys[0], keys[2]), ConnectionOrdering.ordered(keys, listOf("deleted", keys[1], keys[1], keys[0])))
        assertEquals(keys, ConnectionOrdering.ordered(keys, emptyList()))
    }

    @Test fun stalePartialDuplicateAndUnknownOrdersCannotEraseOrReorderAnotherLibrary() {
        ConnectionOrdering.validate(keys, keys.reversed())
        for (invalid in listOf(keys.dropLast(1), keys + keys[0], listOf(keys[0], keys[0], keys[2]), keys.dropLast(1) + "unknown")) {
            assertTrue(runCatching { ConnectionOrdering.validate(keys, invalid) }.isFailure)
        }
        ConnectionOrdering.validate(emptyList(), emptyList())
    }
}
