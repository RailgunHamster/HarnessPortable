package com.harness.portable

import java.util.concurrent.CopyOnWriteArraySet

/**
 * Process-local visibility signal shared by the activity and the tunnel
 * service. The service uses it to apply a short, user-requested background
 * grace period without keeping the device awake indefinitely.
 */
internal object AppVisibility {

    @Volatile
    var isVisible: Boolean = false
        private set

    private val listeners = CopyOnWriteArraySet<(Boolean) -> Unit>()

    fun setVisible(visible: Boolean) {
        if (isVisible == visible) return
        isVisible = visible
        listeners.forEach { it(visible) }
    }

    fun addListener(listener: (Boolean) -> Unit) {
        listeners.add(listener)
        listener(isVisible)
    }

    fun removeListener(listener: (Boolean) -> Unit) {
        listeners.remove(listener)
    }
}
