---
title: "React Effects: Synchronization, Cleanup, and Async Race Conditions"
excerpt: "Use React Effects only for external synchronization, clean them up symmetrically, and prevent stale asynchronous work from updating current UI."
category: "React"

seo:
  focusKeyword: "React useEffect cleanup"
  description: "Use React useEffect for external synchronization with correct dependencies, cleanup, cancellation, race-condition handling, and testing."
  socialTitle: "React Effects: Cleanup Without Race Conditions"
  socialDescription: "Separate rendering and events from external synchronization, then make subscriptions and async work safe across rerenders."
---

# React Effects: Synchronization, Cleanup, and Async Race Conditions

A profile page requests user A, then quickly switches to user B. User B’s response arrives first, but user A’s slower response arrives later and replaces the screen with stale data. The requests were valid; their lifetimes were not connected to the current render.

> **Quick answer:** Use an Effect to synchronize a committed React tree with a system outside React. Put user-triggered work in event handlers and derive values during rendering. For every Effect, define setup, dependencies, and cleanup; cancel or ignore obsolete asynchronous work so an old render cannot update the current UI.

## Effects Are for External Synchronization

Rendering calculates JSX from props and state. Event handlers respond to a specific interaction. Effects run after React commits a render and synchronize with something outside that calculation.

Appropriate Effect work includes:

- subscribing to a browser or application event source;
- connecting to a websocket or third-party widget;
- controlling an imperative media API;
- coordinating a request when the chosen routing or data architecture requires component-level fetching.

This subscription has a symmetric lifetime:

```tsx
import { useEffect, useState } from 'react'

export function OnlineStatus() {
  const [online, setOnline] = useState(() =>
    typeof navigator === 'undefined' ? true : navigator.onLine,
  )

  useEffect(() => {
    const handleOnline = () => setOnline(true)
    const handleOffline = () => setOnline(false)

    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)

    return () => {
      window.removeEventListener('online', handleOnline)
      window.removeEventListener('offline', handleOffline)
    }
  }, [])

  return <p>{online ? 'Online' : 'Offline'}</p>
}
```

React’s [Effect guidance](https://react.dev/learn/synchronizing-with-effects) describes the pattern as setup, dependencies, and optional cleanup.

## Remove Effects That Only Derive State

This Effect creates duplicate state and an extra render:

```tsx
const [fullName, setFullName] = useState('')

useEffect(() => {
  setFullName(`${firstName} ${lastName}`)
}, [firstName, lastName])
```

Calculate the value during rendering instead:

```tsx
const fullName = `${firstName} ${lastName}`
```

Filtering a list, selecting a record, formatting a label, and combining props usually need no Effect. Memoization may help an expensive pure calculation, but it is still not external synchronization.

Likewise, saving because the user clicked a button belongs in that button’s event handler. An Effect that watches `shouldSave` often obscures which interaction caused the mutation and can repeat it after unrelated lifecycle changes.

## Treat Dependencies as Part of the Contract

An Effect must list reactive values it reads:

```tsx
useEffect(() => {
  const connection = chat.connect({ roomId, serverUrl })
  return () => connection.disconnect()
}, [chat, roomId, serverUrl])
```

The dependency array is not a performance wish list. It describes when the synchronization no longer matches the committed render and must be replaced.

Do not suppress the hooks lint rule to preserve a stale closure. Instead:

- move constants outside the component when they are truly static;
- move interaction-specific logic into an event handler;
- stabilize an injected dependency at the composition boundary;
- split one large Effect into independent synchronization processes;
- use a functional state update when the next state depends on the previous state.

Depending on a freshly created object or function can reconnect on every render. Fix ownership before reaching for memoization automatically.

## Clean Up the Previous Synchronization

React calls cleanup before it runs the Effect again with changed dependencies and when the component unmounts. Setup and cleanup should be symmetrical:

```tsx
useEffect(() => {
  const observer = new ResizeObserver(handleResize)
  observer.observe(element)

  return () => observer.disconnect()
}, [element, handleResize])
```

Common pairs include:

| Setup | Cleanup |
| --- | --- |
| `addEventListener` | `removeEventListener` with the same target and callback |
| subscribe | unsubscribe |
| connect | disconnect |
| start interval | clear interval |
| create observer | disconnect observer |
| start request | abort it or ignore its obsolete result |

Cleanup should release only resources created by that Effect run. Avoid a global “remove all listeners” operation that may disrupt another component.

## Abort Obsolete Fetches

Connect each request to the Effect lifetime:

```tsx
type Profile = {
  id: string
  displayName: string
}

export function ProfilePanel({ userId }: { userId: string }) {
  const [profile, setProfile] = useState<Profile>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    const controller = new AbortController()
    setProfile(undefined)
    setError(undefined)

    async function load() {
      try {
        const response = await fetch(
          `/api/profiles/${encodeURIComponent(userId)}`,
          {
            signal: controller.signal,
          },
        )
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        setProfile((await response.json()) as Profile)
      } catch (cause) {
        if (!controller.signal.aborted) {
          setError(cause instanceof Error ? cause.message : 'Request failed')
        }
      }
    }

    void load()
    return () => controller.abort()
  }, [userId])

  if (error) return <p role="alert">{error}</p>
  if (!profile) return <p>Loading profile…</p>
  return <h2>{profile.displayName}</h2>
}
```

When `userId` changes, cleanup aborts the previous request before the next Effect starts. The catch branch distinguishes expected cancellation from a user-visible failure.

Aborting is useful for fetch and resource usage, but not every asynchronous API supports it. A server may also continue processing after the client disconnects. Cancellation is not transactional rollback.

## Ignore Results When Cancellation Is Unavailable

Use an Effect-local flag for an API that cannot be cancelled:

```tsx
useEffect(() => {
  let obsolete = false

  void directory.load(userId).then((result) => {
    if (!obsolete) setProfile(result)
  })

  return () => {
    obsolete = true
  }
}, [directory, userId])
```

The flag belongs to one Effect execution. Each rerun gets its own closure, so an old completion cannot update current state.

Ignoring a result does not stop the underlying work. If the operation is expensive or mutating, prefer an API with cancellation or an architecture that deduplicates and owns requests outside individual components.

## Understand Strict Mode’s Development Check

In development, Strict Mode may perform an extra setup-and-cleanup cycle to expose Effects that leak connections or rely on running only once. The correct response is not a ref that suppresses the second setup. Make cleanup restore the external system so reconnecting is safe.

Production does not use that development check in the same way, but a correct Effect must still tolerate navigation away, remounting, changed dependencies, error recovery, and future concurrent behavior.

## Prefer Router or Data-Layer Fetching for Shared Server State

Manual fetching in Effects has costs: no server execution, easy network waterfalls, no automatic caching, duplicate requests after remount, and repeated race-handling code. The React documentation calls out these limitations directly.

For route-owned data, use the router’s loader mechanism when available. For shared server state, a focused data library or repository layer can own caching, deduplication, cancellation, retries, and invalidation.

An Effect remains appropriate when the component itself owns synchronization with an external widget or browser API. Choose the owner deliberately.

## Test Observable Lifetime Behavior

A useful test does not assert that `useEffect` was called. It proves behavior:

- the subscription starts after render;
- changing a dependency disconnects the previous resource;
- unmounting releases listeners and timers;
- an obsolete request cannot replace newer data;
- cancellation is not shown as an error;
- a real failure remains recoverable and accessible.

Use controllable promises to resolve request B before request A and assert that B remains visible. Spy on `abort`, `disconnect`, or `removeEventListener` only where resource cleanup is part of the component’s contract.

Effects are easiest to reason about when each represents one synchronization process. If the setup, dependencies, and cleanup cannot be stated clearly, the logic may belong in rendering, an event handler, a router, or a dedicated external-state adapter instead.
