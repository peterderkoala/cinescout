# Uptime Kuma's Discord provider and the Discord webhook API

Research for [#137](https://github.com/peterderkoala/cinescout/issues/137) (map [#136](https://github.com/peterderkoala/cinescout/issues/136)), 2026-10-19.

Sources are pinned:

- Uptime Kuma at commit [`b092505f`](https://github.com/louislam/uptime-kuma/tree/b092505f894d66669b67f46e1da72b51016eceab) (`master`, 2026-10-19). Shortened to `UK@b092505` below.
- Discord docs from [discord/discord-api-docs@`f7416a2c`](https://github.com/discord/discord-api-docs/tree/f7416a2cf165455f72a31f5ea4e671a4f047f2f7) (`main`, 2026-10-19). Shortened to `DD@f7416a2` below. That repo is the source of discord.com/developers/docs.
- The .NET resilience defaults come from Microsoft Learn ([http-resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults)).

## 1. What Uptime Kuma's Discord provider sends

Source: [`server/notification-providers/discord.js`](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/server/notification-providers/discord.js).

### Common to every message

| Aspect | Behaviour | Line |
|---|---|---|
| Transport | `axios.post(webhookUrl, json)`. No `?wait=true`, no `allowed_mentions`, no handling of 429 or retries. | 65, 91, 118, 175, 255 |
| `username` | Always sent: `discordUsername \|\| "Uptime Kuma"`. | 22 |
| `avatar_url` | Before every send it does a **GET on the webhook URL** (Discord's unauthenticated "Get Webhook with Token" call). It sends `avatar_url` = the Uptime Kuma icon only when the webhook has no avatar of its own. If the GET fails, it assumes the webhook has an avatar. **The form has no avatar field.** | 28–36, 56–58 |
| Suppress notifications | `flags = 1 << 12` (4096, `SUPPRESS_NOTIFICATIONS`) when `discordSuppressNotifications` is set. It applies to every format, test messages included. | 18, 62–64 |
| Thread/forum | `?thread_id=` (`postToThread`) or the `thread_name` body field (`createNewForumPost`). Out of scope for us. | 24–26, 59–61 |
| Errors | `throwGeneralAxiosError` throws an `Error` whose message is `"<axios message> (code=…) (HTTP <status> <statusText>) <response body as JSON>"`, so Discord's validation body reaches the user. | [notification-provider.js L121–143](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/server/notification-providers/notification-provider.js#L121-L143) |
| URL handling | Only `new URL(notification.discordWebhookUrl)`, which throws on garbage. **No host or path validation anywhere.** The save path (`Notification.save`) stores the form JSON as it is. | 23; [notification.js L275–302](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/server/notification.js#L275-L302) |

### Message formats (`discordMessageFormat`: `normal` default, `minimalist`, `custom`)

- **minimalist**: `content` only, either `"🔴 <name> is down."` or `"🟢 <name> is up."`. It uses no embed and **no prefix**. (L72–93)
- **custom**: `content` = the rendered Liquid template, with no embed. (L95–120)
- **normal** (the embeds we want to copy):

**DOWN** (L122–175)

```json
{
  "username": "Uptime Kuma",
  "content": "<discordPrefixMessage, only if set>",
  "flags": 4096,
  "embeds": [{
    "title": "❌ Your service <name> went down. ❌",
    "color": 16711680,
    "timestamp": "<heartbeat time, ISO>",
    "fields": [
      { "name": "Service Name", "value": "<name>" },
      { "name": "Service URL", "value": "<address>" },
      { "name": "Went Offline", "value": "<t:UNIX:F>" },
      { "name": "Time (<server tz>)", "value": "<localDateTime>" },
      { "name": "Error", "value": "<msg or N/A>" }
    ]
  }]
}
```

The flags field is present only when suppress is on. The "Service URL" field is left out when `disableUrl` is set or when the monitor has no address; push monitors label it "Service Type".

**UP** (L177–255): the title is `"✅ Your service <name> is up! ✅"` and the colour is `65280` (`#00FF00`). The fields are Service Name, the optional Service URL, the optional "Went Offline" `<t:…:F>`, the optional "Downtime Duration" (`1h 23m` / `45m 30s`, from `formatDuration`), `Time (<tz>)`, and the optional "Ping" `<n> ms`. The prefix and flags work as for DOWN.

Details worth copying:

- Fields never set `inline`, so each field sits on its own row.
- Times use Discord's `<t:unix:F>` markdown, which the reader's client renders in the reader's own timezone. A second field adds the server-local time as plain text.
- The **prefix message goes in `content`** next to the embed. That is the only way a mention can ping anyone: mentions inside embeds never notify.

### Test message (L41–67)

When `heartbeatJSON == null`, the provider sends a test: **plain `content` with no embed**. The content is `msg`, `"Test: " + msg` for minimalist, or the rendered template for custom. Username, avatar and flags apply as usual, but **the prefix message is not added**. Because the test has no embed, it never exercises the embed payload.

## 2. The settings form

Source: [`src/components/notifications/Discord.vue`](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/src/components/notifications/Discord.vue). The shared dialog ([`NotificationDialog.vue`](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/src/components/NotificationDialog.vue)) adds "Friendly Name" (default "My Discord Alert (1)" via `getUniqueDefaultName`), "Default enabled" and "Apply on all existing monitors".

| Field | Control | Default | Validation |
|---|---|---|---|
| Discord Webhook URL | `HiddenInput` (masked with a show toggle), `required` | — | HTML `required` only |
| Bot Display Name | text, placeholder = app name | empty, so "Uptime Kuma" | none |
| Prefix Custom Message | text, placeholder "Hello @everyone is…" | empty | none |
| Message format | select normal/minimalist/custom | `normal` | — |
| Message template | Liquid textarea (custom only) | — | none |
| Message type | select channel/forum post/thread | `channel` | — |
| Disable URL in Notification | switch | `false` | — |
| Suppress Notifications | switch, help text "messages will be posted to the channel but won't trigger push or desktop notifications" | `false` | — |

The defaults are set in `mounted()` (L154–171). The URL help text says "Server Settings -> Integrations -> View Webhooks -> New Webhook" ([en.json](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/src/lang/en.json)).

## 3. How "Send test" works

1. The dialog's `test()` emits the socket event `testNotification` with **the current, unsaved form object**, `this.notification` ([NotificationDialog.vue L483–489](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/src/components/NotificationDialog.vue#L483-L489)). The buttons are disabled while `processing`.
2. The server runs `Notification.send(notification, notification.name + " Testing")` ([server.js L1231–1249](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/server/server.js#L1231-L1249)). The test text is therefore `"<Friendly Name> Testing"`.
3. Success returns `{ok:true, msg:"Sent Successfully."}`. Any thrown error returns `{ok:false, msg:e.message}`, which is the axios error string with HTTP status and Discord's JSON body.
4. The client shows the result as a toast through `toastRes` (`toast.success` / `toast.error`) ([mixins/socket.js L349–359](https://github.com/louislam/uptime-kuma/blob/b092505f894d66669b67f46e1da72b51016eceab/src/mixins/socket.js#L349-L359)).

This works for Uptime Kuma because it sends the **full webhook URL back to the browser** (the masked `HiddenInput` still holds the real value).

## 4. Discord execute-webhook facts

`POST /webhooks/{id}/{token}`: [webhook.mdx L206–260](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/resources/webhook.mdx#L206-L260)

- At least one of `content`, `embeds`, `components`, `file` or `poll` is required.
- `content` is at most 2000 characters. `embeds` holds at most 10 embed objects. `username` and `avatar_url` are per-message overrides.
- `flags`: only `SUPPRESS_EMBEDS`, `SUPPRESS_NOTIFICATIONS` (`1 << 12` = 4096) and `IS_COMPONENTS_V2` can be set ([message.mdx L157](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/resources/message.mdx#L157)). Suppress "will disable push notifications and only cause a notification badge" (L720).
- `?wait=true` "waits for server confirmation of message send before response, and returns the created message body (defaults to `false`; **when `false` a message that is not saved does not return an error**)". Without it, success is `204 No Content`.
- In webhook embeds you can set every field except `type`, `provider`, `video` and the image size/proxy fields.
- Discord may strip characters from content. The docs advise using `allowed_mentions` when strings come from users.

**Embed limits** ([message.mdx L605–621](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/resources/message.mdx#L605-L621)). All limits are inclusive and measured after trimming whitespace:

| Field | Limit |
|---|---|
| title | 256 |
| description | 4096 |
| fields | 25 |
| field.name | 256 |
| field.value | 1024 |
| footer.text | 2048 |
| author.name | 256 |

On top of that, the sum of title, description, field names and values, footer and author across **all embeds** of a message must be ≤ 6000 characters. Breaking any of these limits returns `400 Bad Request`. Embeds with the same URL are deduplicated. `color` is an integer and `timestamp` is ISO8601 (L498–499).

**`allowed_mentions`** ([message.mdx L714–800](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/resources/message.mdx#L714-L800))

- The mention types are `users`, `roles` and `everyone`. **`everyone` covers both `@everyone` and `@here`.**
- The documented default is `{"parse":["users","roles","everyone"]}` for regular messages, but "In **interactions** and **webhooks**, only user mentions are parsed", which equals `{"parse":["users"]}`. Taken literally, a prefix of `@here` sent without `allowed_mentions` does **not** ping. Uptime Kuma sends no `allowed_mentions`, yet its placeholder suggests `@everyone` works. Real-world behaviour may differ from the docs. **This is not verified against a live webhook.**
- A mention notifies only if it is also in the content (embeds don't count). `parse` cannot be combined with an explicit `roles`/`users` list for the same type. Mentioning `@everyone`/`@here` also needs the `MENTION_EVERYONE` permission. For a webhook, that is the permission of whoever created it.
- **Implication:** send `allowed_mentions` explicitly whenever a prefix is set, for example `{"parse":["everyone","roles","users"]}`. That removes the doubt in both directions.

**Rate limits** ([rate-limits.mdx](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/topics/rate-limits.mdx))

- Limits are not documented as numbers ("should not be hard coded"). Webhooks are a top-level resource, so each webhook (`webhook_id + token`) has its own bucket.
- Every response carries `X-RateLimit-Limit/-Remaining/-Reset/-Reset-After/-Bucket`.
- A 429 carries `Retry-After` (seconds) and a JSON body `{message, retry_after: float, global, code?}`, plus `X-RateLimit-Scope` (`user`/`global`/`shared`). `shared` 429s don't count against you. The docs' own examples use `Retry-After: 65` and `1337`, so waits can be long.
- Without an auth header, the global limit (50 req/s) applies **per IP**.
- 10,000 invalid requests (401/403/429) per 10 minutes gets the IP temporarily banned (Cloudflare).
- **"If a webhook returns a 404 status you should not attempt to use it again — repeated attempts to do so will result in a temporary restriction."**

**URL hosts and shapes**

- The documented base URL is only `https://discord.com/api`, with an optional `/v{n}` version segment that falls back to the default version when left out ([reference.mdx L10–36](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/reference.mdx#L10-L36)). The webhook `url` example is `https://discord.com/api/webhooks/{id}/{token}` ([oauth2.mdx](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/topics/oauth2.mdx)).
- `discordapp.com` is the legacy domain. Since 2023-10-17 Discord puts a separate global rate limit on it and says "please make sure you are making calls to `discord.com/*`" ([change-log.mdx L3158–3172](https://github.com/discord/discord-api-docs/blob/f7416a2cf165455f72a31f5ea4e671a4f047f2f7/developers/change-log.mdx)).
- `canary.discord.com` / `ptb.discord.com` are **not mentioned in the API docs**. The Canary/PTB clients may copy webhook URLs with those hosts; this is not verified.
- A defensible allowlist: scheme `https`; host `discord.com`, `discordapp.com`, `canary.discord.com` or `ptb.discord.com`; path matching `^/api(/v\d+)?/webhooks/\d+/[\w-]+$`. Optionally rewrite the host to `discord.com` before sending.

## 5. Clashes with CineScout's `DiscordNotifier`

Today's code is [`DiscordNotifier.cs`](../../src/cinescout.core/Discord/DiscordNotifier.cs), registered with a plain `.AddStandardResilienceHandler()` in [`CoreServiceCollectionExtensions.AddDiscord`](../../src/cinescout.core/Extensions/CoreServiceCollectionExtensions.cs). The standard handler's defaults: retries on 408, 429, ≥500, `HttpRequestException` and `TimeoutRejectedException`. That is 3 retries with exponential backoff and jitter, starting at 2 s, for **all methods including POST**. It **honours `Retry-After`** (`ShouldRetryAfterHeader = true`). The attempt timeout is 10 s and the total timeout 30 s. The circuit breaker trips at a 10% failure ratio with at least 100 requests per 30 s, and breaks for 5 s ([MS Learn](https://learn.microsoft.com/dotnet/core/resilience/http-resilience#standard-resilience-handler-defaults)).

1. **POST retries can post duplicates.** On a 5xx or a timeout after Discord has already stored the message, the retry posts it again. Uptime Kuma never retries. Options: accept it (it is rare), or use `options.Retry.DisableForUnsafeHttpMethods()` and keep only a 429-aware retry.
2. **429 vs. the 30 s total timeout.** Discord's `Retry-After` can be longer than 30 s. The handler waits, the total timeout fires, and the caller gets a `TimeoutRejectedException`, which the notifier already catches. The contract holds, but a "Send test" from the UI can hang for about 30 s. A test endpoint should use a shorter timeout and no retries, or turn the 429 into a clear message right away.
3. **Never-throws gaps.** The `catch` covers `HttpRequestException`, `InvalidOperationException`, `TaskCanceledException` and `TimeoutRejectedException`. It misses Polly's `BrokenCircuitException` (thrown when the breaker is open) and `UriFormatException` (a malformed absolute URL). Once users type the URLs in, malformed input becomes likely. Validate the URL on save, and widen the catch, or catch `Exception` minus cancellation.
4. **One pipeline for all channels.** The breaker and the rate limiter belong to the typed client, not to a webhook. A dead channel (404) counts towards breaking for everyone. At CineScout's volume it will never reach 100 requests per 30 s, so the breaker never trips, but this is worth knowing.
5. **Silent loss without `?wait=true`.** Discord documents that with `wait=false` "a message that is not saved does not return an error". The NotificationLog would record a success that never happened. Append `wait=true`. It costs nothing, because the response body is a message object we can ignore.
6. **The failure detail is too thin for "Send test".** The current `Detail` is only `"<code> <reason>"`. Discord's 400 body names the field that failed (Uptime Kuma passes it straight through). Read the body, at least on failures, for the test result and the log.
7. **404 means the webhook is gone.** Discord asks clients to stop using it. Consider marking the channel broken or disabling it on a 404 instead of retrying on every notification.
8. **Avatar lookup.** Uptime Kuma's extra GET on every send exists only to decide on a default avatar. CineScout has an explicit avatar override in scope, so it sends `avatar_url` only when the user set one. Skip the GET.
9. **Send test with a masked URL.** Uptime Kuma tests unsaved values because the browser holds the full URL. In CineScout, `/api` never returns the URL. A "test unsaved form" endpoint therefore has to take the URL when it changed and otherwise fall back to the saved channel's URL by id. A "test saved channel" endpoint is simpler.

## What this means for the map's open questions

- **Webhook URL validation:** Uptime Kuma validates nothing. CineScout needs an allowlist (https; the `discord.com` family; `/api[/vN]/webhooks/{id}/{token}`) because the server POSTs to a URL the user supplied. Validate on save, which also closes never-throws gap 3.
- **Send test:** Uptime Kuma tests the unsaved values with plain content and no prefix or embed, and shows the raw error in a toast. A CineScout test that sends a **sample embed of a real `NotificationType`** would catch embed-limit 400s, which Uptime Kuma's test cannot.
- **Prefix mentions:** add explicit `allowed_mentions` whenever a prefix is set. The documented webhook default does not parse `@here`/`@everyone`.
- **Resilience:** add `?wait=true`, decide about POST retries, and give the test a short budget with no retries.
