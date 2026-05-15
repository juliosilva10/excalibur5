# Deriv API - Documentação Completa

> Referência técnica para desenvolvimento
> Extraído de: https://developers.deriv.com

---

## Índice

1. [Introduction](#introduction)
2. [API overview](#api-overview)
3. [Authentication](#authentication)
4. [OAuth 2.0](#oauth-2-0)
5. [Markup](#markup)
6. [Options Setup](#options-setup)
7. [Get accounts](#get-accounts)
8. [Create account](#create-account)
9. [Reset demo balance](#reset-demo-balance)
10. [WebSockets](#websockets)
11. [Account Management](#account-management)
12. [Balance](#balance)
13. [Portfolio](#portfolio)
14. [Profit table](#profit-table)
15. [Statement](#statement)
16. [Transaction](#transaction)
17. [Market Data](#market-data)
18. [Active symbols](#active-symbols)
19. [Contracts for](#contracts-for)
20. [Contracts list](#contracts-list)
21. [Ticks](#ticks)
22. [Ticks history](#ticks-history)
23. [Trading Operations](#trading-operations)
24. [Proposal](#proposal)
25. [Buy](#buy)
26. [Sell](#sell)
27. [Proposal open contract](#proposal-open-contract)
28. [Contract update](#contract-update)
29. [Contract update history](#contract-update-history)
30. [Cancel](#cancel)
31. [Subscription Management](#subscription-management)
32. [Forget](#forget)
33. [Forget all](#forget-all)
34. [System](#system)
35. [Ping](#ping)
36. [Time](#time)
37. [Trading times](#trading-times)
38. [Health check](#health-check)
39. [Workflows](#workflows)

---

## Introduction

*Fonte: https://developers.deriv.com/docs/*

# Deriv API Documentation

Build custom trading applications using Deriv's REST APIs for account management and WebSocket APIs for real-time trading and execution.

## Getting Started

New to the Deriv API? Start with these essential guides to understand the fundamentals:

### [API OverviewLearn how REST and WebSocket APIs work together.Explore](</docs/intro/api-overview/>)### [AuthenticationLearn how to authenticate requests and securely access user accounts.Explore](</docs/intro/authentication/>)

## API Endpoints

Explore our comprehensive API endpoints organised by category:

[RESTOptions SetupOptions account creation and managementExplore](</docs/options/>)[WebSocketAccount ManagementAuthentication and account managementExplore](</docs/account/>)[WebSocketMarket DataMarket data and symbolsExplore](</docs/data/>)[WebSocketTrading OperationsBuy, sell, and manage contractsExplore](</docs/trading/>)[WebSocketSubscription ManagementManage WebSocket subscriptionsExplore](</docs/subscription/>)[WebSocketSystemSystem health and monitoringExplore](</docs/system/>)

[API Overview](</docs/intro/api-overview/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#1475647d39676164647b6660547071667d623a777b79>)

---

## API overview

*Fonte: https://developers.deriv.com/docs/intro/api-overview/*

[Getting Started](</docs>)

# API Overview

Understanding the Deriv API ecosystem and its core capabilities.

## Introduction

Deriv API provides programmatic access to trading services, account management, and market data for the Options trading platform. The API enables developers to integrate Deriv's robust trading infrastructure into their applications using REST endpoints for account setup and WebSocket connections for real-time trading.

##### Key features

  * Options account creation and management (REST)
  * Real-time market data streaming (WebSocket)
  * Contract trading (buy, sell, update) (WebSocket)
  * Portfolio and account monitoring (WebSocket)
  * Historical data access (WebSocket)
  * Subscription-based real-time updates (WebSocket)
  * Secured access via **OAuth 2.0** or **Personal Access Token (PAT)**


## API Architecture

The Deriv API consists of two complementary components designed to work together seamlessly:

### REST APIs

  * Account creation and management
  * OTP generation for WebSocket authentication
  * System health monitoring
  * Standard HTTP methods (GET, POST)
  * Stateless requests
  * Authentication via `Deriv-App-ID` header + `Authorization: Bearer` token (OAuth 2.0 or PAT)


### WebSocket APIs

  * Real-time trading operations (buy, sell, proposals)
  * Live market data streaming (ticks, symbols)
  * Account data subscriptions (balance, portfolio)
  * Persistent bidirectional connection
  * Real-time push notifications
  * Three endpoint types: **public** (no auth), **demo** , and **real** (both authenticated via OTP)


## When to use REST vs WebSocket

Feature| REST API| WebSocket API  
---|---|---  
Use Case| Account setup and management| Real-time trading and market data  
Connection Type| Stateless HTTP requests| Persistent connection  
Authentication| Deriv-App-ID header + Bearer token (OAuth 2.0 or PAT)| OTP-based (obtained via REST API); public endpoint requires no auth  
Real-time Updates| No (request-response only)| Yes (subscriptions)  
Examples| `POST /trading/v1/options/accounts`, `POST .../{accountId}/otp`, `GET /v1/health`| Buy contract, stream ticks, get balance  
  
##### Typical workflow

  1. **REST:** Get an authenticated WebSocket URL via the OTP endpoint (requires your Bearer token)
  2. **WebSocket:** Connect using the authenticated URL returned in the OTP response
  3. **WebSocket:** Perform real-time trading operations


**Note:** Users receive a default demo account upon signup — you do not need to create one via the API before trading.

## API Endpoints

**REST base URL:**
    
    
     1https://api.derivws.com

**WebSocket endpoints:**
    
    
     1# Public (no authentication required)
    2wss://api.derivws.com/trading/v1/options/ws/public
    3
    4# Demo account (authenticated via OTP)
    5wss://api.derivws.com/trading/v1/options/ws/demo?otp=YOUR_OTP
    6
    7# Real account (authenticated via OTP)
    8wss://api.derivws.com/trading/v1/options/ws/real?otp=YOUR_OTP

The OTP is obtained by calling `POST /trading/v1/options/accounts/{accountId}/otp` — the response contains a ready-to-use WebSocket URL with the OTP already embedded.

**Connection requirements:**

  * WebSocket-capable client (browser or server-side)
  * Stable internet connection
  * Valid `Deriv-App-ID` and Bearer token for REST calls and OTP generation


## Next steps

Continue with these guides to start building with the Deriv API:

### [AuthenticationCompare OAuth 2.0 and PAT authentication methods and choose the right one for your use case.Explore](</docs/intro/authentication/>)### [OAuth 2.0Step-by-step guide to the OAuth 2.0 Authorization Code flow with PKCE.Explore](</docs/intro/oauth/>)### [Complete WorkflowsEnd-to-end examples combining REST and WebSocket APIs for common trading scenarios.Explore](</docs/workflows/>)### [Options REST APIFull reference for all REST endpoints: accounts, OTP, WebSocket setup, and more.Explore](</docs/options/>)

[Introduction](</docs/>)[Authentication](</docs/intro/authentication/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#2d4c5d44005e585d5d425f596d49485f445b034e4240>)

---

## Authentication

*Fonte: https://developers.deriv.com/docs/intro/authentication/*

[Getting Started](</docs>)

# Authentication

To unlock the complete functionality of Deriv APIs, you must authenticate and authorise your users. Deriv supports two approaches: **OAuth 2.0 apps** and **Personal Access Token (PAT) apps**.

## Authentication Methods

### OAuth 2.0 apps

OAuth 2.0 lets users grant your app access without sharing their password. Your app redirects the user to a Deriv sign-in and consent page. After approval, Deriv returns an authorization code which you exchange for an access token.

1

Redirect to Deriv

2

User Logs In

3

Code Returned

4

Exchange for Token

[Set up OAuth 2.0](</docs/intro/oauth/>)

### PAT apps

With a PAT app, the user generates a Personal Access Token in Deriv and manually enters it into your application. The app stores the token and includes it in API requests as a bearer token.

1

Generate Token

2

Paste into App

3

App Uses Token

##### Token-based access

Both methods give your app tokens to make authenticated API calls. The key difference is how users sign in and onboard.

## Why Authentication Matters

Authentication improves security by keeping user passwords out of third-party apps. Tokens limit access based on scopes and can be revoked independently if needed.

OAuth 2.0 provides a standardized flow with short-lived access tokens to enhance security and user experience. PATs provide a simpler authentication option when manual token entry is acceptable.

## OAuth 2.0 app vs PAT app

Aspect| OAuth 2.0 app| PAT app  
---|---|---  
Best fit| Web-based applications| Desktop/native and non-web contexts  
How onboarding works| User redirected to Deriv OAuth 2.0 sign-in; after approval, redirected back with an authorization code.| User generates a PAT in Deriv and manually pastes it into the app.  
Redirect URLs| Required for completing the flow.| Not used.  
User experience| Seamless web sign-in with consent.| Manual token entry — simple but less automated.  
Use case examples| Web dashboards, browser apps.| Desktop tools, CLI apps, native clients.  
Security notes| Issues short-lived tokens and minimises long-term credential sharing.| PATs act like scoped API credentials and can be revoked independently.  
  
## When to choose which

  1. **Choose OAuth 2.0 app** when your product can handle browser redirects and you need a standard delegated flow with user authorisation.
  2. **Choose PAT app** when browser redirects are not practical and manual token entry is acceptable, such as in desktop or native environments.


[API Overview](</docs/intro/api-overview/>)[OAuth 2.0](</docs/intro/oauth/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#1677667f3b65636666796462567273647f603875797b>)

---

## OAuth 2.0

*Fonte: https://developers.deriv.com/docs/intro/oauth/*

[Getting Started](</docs>)

# OAuth 2.0

A complete guide to implementing Login and Sign Up using Deriv's OAuth 2.0 Authorization Code flow with PKCE.

## How the flow works

1

Generate PKCE

2

Redirect to Deriv

3

User Authenticates

4

Exchange Code

5

Use Token

  1. **Generate PKCE** — Create a `code_verifier` (random string) and derive `code_challenge` = BASE64URL(SHA256(code_verifier)). Also generate a random `state` for CSRF protection.
  2. **Redirect to Deriv** — Send the user to Deriv's authorization URL with all required parameters.
  3. **User authenticates** — Deriv shows either the login or registration form. All login and consent screens are managed by the OAuth provider.
  4. **Redirect back** — Deriv redirects the user to your `redirect_uri` with an authorization `code` and `state`.
  5. **Verify state** — Confirm the returned `state` matches what you stored. This prevents CSRF attacks.
  6. **Exchange code for token** — Your backend sends the `code` + `code_verifier` to Deriv's token endpoint and receives an `access_token`.
  7. **Use the token** — Make authenticated API calls using the Bearer token.


## Before you start

You need:

  * A registered OAuth2 client from Deriv with a `client_id` and a pre-registered `redirect_uri`.
  * HTTPS enabled on your redirect URL.
  * Your app must handle redirects, read the authorization code, and exchange it for tokens.


## Step 1: Generate PKCE parameters

##### What is PKCE?

**PKCE** (Proof Key for Code Exchange, pronounced “pixy”) prevents authorization code interception attacks. Even if an attacker intercepts the authorization code, they cannot exchange it without the original `code_verifier` that only your app generated and stored.

Term| What it is  
---|---  
`code_verifier`| A cryptographically random string (43–128 characters) generated by your app  
`code_challenge`| `BASE64URL(SHA256(code_verifier))` — sent with the authorization request  
`code_challenge_method`| Always S256 (SHA-256)  
  
**Why it works:** Only the app that generated the `code_verifier` can complete the token exchange.

### Generating PKCE in JavaScript
    
    
    // 1. Generate a random code_verifier
    const array = crypto.getRandomValues(new Uint8Array(64));
    const codeVerifier = Array.from(array)
      .map(v => 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~'[v % 66])
      .join('');
    
    // 2. Derive the code_challenge
    const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(codeVerifier));
    const codeChallenge = btoa(String.fromCharCode(...new Uint8Array(hash)))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
    
    // 3. Generate a random state for CSRF protection
    const state = crypto.getRandomValues(new Uint8Array(16))
      .reduce((s, b) => s + b.toString(16).padStart(2, '0'), '');
    
    // 4. Store code_verifier and state before redirecting
    sessionStorage.setItem('pkce_code_verifier', codeVerifier);
    sessionStorage.setItem('oauth_state', state);

Required authorization request parameters:

  * response_type=code
  * client_id
  * redirect_uri
  * scope
  * state
  * code_challenge + code_challenge_method=S256 (PKCE)


##### Storage tip

Store the `code_verifier` and `state` in `sessionStorage` before redirecting — they survive the redirect and are automatically cleared when the tab is closed. Clear them from storage immediately after a successful token exchange.

## Step 2: Redirect the user to the authorization endpoint

Send users to Deriv's OAuth 2.0 authorization endpoint:
    
    
    https://auth.deriv.com/oauth2/auth

### Login

Login uses the standard OAuth2 + PKCE parameters with no additions.

#### Parameters

Parameter| Value| Description  
---|---|---  
`response_type`Required| `code`| Request an authorization code  
`client_id`Required| `Your app ID`| Registered OAuth2 application ID from Deriv  
`redirect_uri`Required| `Your callback URL`| Must exactly match the URI registered with Deriv  
`scope`Required| `trade account_manage`| Requested permissions  
`state`Required| `Random string`| CSRF protection — generate a new value for each request  
`code_challenge`Required| `BASE64URL(SHA256(verifier))`| The PKCE challenge derived from code_verifier  
`code_challenge_method`Required| `S256`| Always SHA-256  
`app_id`Optional| `Your legacy app ID`| Your V1 app ID from the Legacy Deriv API — include this only if you also maintain a legacy API app  
  
#### Login URL
    
    
    https://auth.deriv.com/oauth2/auth?
      response_type=code
      &client_id={YOUR_CLIENT_ID}          # e.g. app12345
      &redirect_uri={YOUR_REDIRECT_URI}    # e.g. https://yourapp.com/callback
      &scope=trade+account_manage
      &state={RANDOM_STATE}                # e.g. abc123random
      &code_challenge={PKCE_CHALLENGE}     # e.g. E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
      &code_challenge_method=S256

##### Also maintaining a Legacy API app?

If you also have an app on the Legacy Deriv API, append `&app_id=YOUR_LEGACY_APP_ID` to the login URL (and sign up URL). Deriv will check whether the user belongs to the old or new platform and route them to the appropriate version of your app.

#### Login URL with legacy app support
    
    
    https://auth.deriv.com/oauth2/auth?
      response_type=code
      &client_id={YOUR_CLIENT_ID}
      &redirect_uri={YOUR_REDIRECT_URI}
      &scope=trade+account_manage
      &state={RANDOM_STATE}
      &code_challenge={PKCE_CHALLENGE}
      &code_challenge_method=S256
      &app_id={YOUR_LEGACY_APP_ID}      # V1 app ID from legacy-api.deriv.com

### Sign Up

Sign up uses the same base URL and parameters as login, plus one additional required parameter:

#### Required sign up parameter

Parameter| Value| Description  
---|---|---  
`prompt`Required| `registration`| Always this exact value. Tells Deriv to show the signup form instead of login.  
  
#### Optional partner attribution parameters

The following parameters are all optional and managed in the Partners dashboard. Include them to attribute signups to your partner account. The tracking token parameter has four equivalent names (`t`, `affiliate_token`, `sidi`, `ca`) — use whichever one appears in your referral link or Partners dashboard.

Parameter| Value| Purpose  
---|---|---  
`t``affiliate_token``sidi``ca`| Your affiliate tracking token| Tracking and attribution. Use **only one** of these parameter names — they are equivalent aliases. Pick the one that appears in your referral link or in the Partners dashboard.  
`utm_campaign`| Your campaign name| Identifies the marketing campaign  
`utm_medium`| affiliate| Indicates a partner integration  
`utm_source`| Your affiliate ID| Commission tracking and reporting  
  
##### Which tracking parameter should I use?

`t`, `affiliate_token`, `sidi`, and `ca` all serve the same purpose. Use the one that appears in your Deriv referral link or in your Partners dashboard — don't include more than one.

#### Sign Up URL
    
    
    https://auth.deriv.com/oauth2/auth?
      response_type=code
      &client_id={YOUR_CLIENT_ID}          # e.g. app12345
      &redirect_uri={YOUR_REDIRECT_URI}    # e.g. https://yourapp.com/callback
      &scope=trade+account_manage
      &state={RANDOM_STATE}                # e.g. abc123random
      &code_challenge={PKCE_CHALLENGE}     # e.g. E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
      &code_challenge_method=S256
      &prompt=registration
      &t={YOUR_TRACKING_TOKEN}             # or: affiliate_token | sidi | ca — use the name from your referral link
      &utm_campaign={YOUR_CAMPAIGN}        # e.g. dynamicworks
      &utm_medium=affiliate
      &utm_source={YOUR_AFFILIATE_ID}      # e.g. CU303219

##### Important

Always validate the `state` parameter on return and generate your `code_challenge` from a secure random `code_verifier`. Never reuse these values between requests.

## Step 3: Handle the callback

Whether the user logged in or signed up, the callback works exactly the same way. After authentication, Deriv redirects to your `redirect_uri`:
    
    
    https://yourapp.com/callback?code=AUTHORIZATION_CODE&state=RANDOM_STATE

If something went wrong:
    
    
    https://yourapp.com/callback?error=access_denied&error_description=User+cancelled

### Your app must:

  1. **Verify the state** — compare the `state` from the URL with the value you stored before the redirect. If they don't match, abort — it may be a CSRF attack.
  2. **Extract the code** — read the `code` query parameter.


##### The authorization code is single-use and expires quickly

Exchange it immediately. Do not store or log authorization codes.

## Step 4: Exchange code for tokens

Make a POST request from your **backend** to the token endpoint. Never perform the token exchange from the browser.
    
    
    POST https://auth.deriv.com/oauth2/token

### Request body (form-encoded)
    
    
    grant_type=authorization_code
    client_id=YOUR_CLIENT_ID
    code=AUTH_CODE_FROM_CALLBACK
    code_verifier=YOUR_ORIGINAL_CODE_VERIFIER
    redirect_uri=https://your-app.com/callback

### cURL example
    
    
    curl -X POST https://auth.deriv.com/oauth2/token \
      -H "Content-Type: application/x-www-form-urlencoded" \
      -d "grant_type=authorization_code" \
      -d "client_id=YOUR_CLIENT_ID" \
      -d "code=AUTH_CODE" \
      -d "code_verifier=YOUR_CODE_VERIFIER" \
      -d "redirect_uri=https://your-app.com/callback"

### Token response
    
    
    {
      "access_token": "ory_at_...",
      "expires_in": 3600,
      "token_type": "Bearer"
    }

## Step 5: Use the access token in API calls

Include the access token as a Bearer token in the `Authorization` header for all API calls:


### Example
    
    
    curl -X GET "https://api.derivws.com/trading/v1/options/accounts" \
      -H "Authorization: Bearer YOUR_ACCESS_TOKEN"

## Quick reference

Endpoint| URL  
---|---  
Authorization| `https://auth.deriv.com/oauth2/auth`  
Token exchange| `https://auth.deriv.com/oauth2/token`  
API base URL| `https://api.derivws.com`  
  
Where to find your values:

Value| Where  
---|---  
`client_id`| Register an OAuth2 app with Deriv — you'll receive an app ID  
`redirect_uri`| Set during app registration — must match exactly  
`t / affiliate_token / sidi / ca (signup)`| Your referral link or the Partners dashboard — use the exact parameter name shown there  
`utm_source / affiliate ID (signup)`| Managed and set in the Partners dashboard  
`utm_campaign (signup)`| Managed and set in the Partners dashboard  
`app_id (legacy)`| Your V1 app ID from legacy-api.deriv.com — only needed if you maintain a Legacy API app  
  
## Troubleshooting

Problem| Likely cause| Fix  
---|---|---  
State mismatch error| state in the callback doesn't match stored value| Store state in sessionStorage before redirecting, and don't regenerate it on page load  
invalid_grant on token exchange| code_verifier doesn't match the challenge, or code expired/already used| Send the original code_verifier, not a newly generated one; exchange the code immediately  
Redirect URI mismatch| URL doesn't exactly match what's registered| Check for trailing slashes, http vs https, port numbers  
invalid_client| Wrong client_id| Verify your credentials from the Deriv dashboard  
Login form shows instead of signup| Missing prompt=registration| Add prompt=registration to the authorization URL  
Signup not tracked to partner| Missing or wrong UTM parameters| Verify your tracking token parameter (one of t, affiliate_token, sidi, or ca) matches the one shown in your referral link, and that utm_source, utm_medium, and utm_campaign are all present and correct  
  
## Implementation checklist

### Login

  * `response_type` is `code`
  * `client_id` and `redirect_uri` are registered with Deriv
  * `code_challenge` and `state` are generated fresh for each request
  * `code_verifier` is stored in `sessionStorage` before redirect
  * Callback verifies `state` before exchanging the code
  * Token exchange happens server-side (not in the browser)
  * `code_verifier` is cleared from storage after use
  * If maintaining a legacy app, `app_id` is set to your Legacy app ID (optional)


### Sign Up (additional)

  * `prompt` is set to `registration` (required)
  * Tracking token (one of `t`, `affiliate_token`, `sidi`, `ca`), `utm_source`, `utm_campaign`, and `utm_medium` are set if needed — use the parameter name shown in your referral link or Partners dashboard (optional)


[Authentication](</docs/intro/authentication/>)[Markup](</docs/intro/markup/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#cdacbda4e0beb8bdbda2bfb98da9a8bfa4bbe3aea2a0>)

---

## Markup

*Fonte: https://developers.deriv.com/docs/intro/markup/*

[Getting Started](</docs>)

# Mark-up

Earn markup commission on every contract your users execute when they trade via your Deriv API app or platform.

If you build a platform that allows users to trade using Deriv APIs, you can earn up to **5% markup commission for a limited time** on every contract your users execute.

## Example

Let's say a user places a **$10 stake** , and the potential payout without any markup is **$17.20** if the trade wins.

If you set a **2% markup** , your commission will be calculated on the potential payout:

**2% of $17.20 = $0.34**

With this adjusted stake, the user's new potential payout becomes: **$16.63**

##### Markup impacts user profit

The higher the markup you set, the lower your user's payout will be on winning trades.

## Another example

  * **Stake:** USD 25.50
  * **Payout:** USD 50
  * **Markup:** 2% of USD 50 = USD 1
  * **Client balance debited:** USD 26.50


## Other ways to monetise the Deriv API

  1. **Charge for access** — Subscription or one-time fee.
  2. **Offer premium features** — Advanced analytics or real-time data.
  3. **Affiliate marketing** — Earn commissions by referring new users.
  4. **Referral fees** — Reward users who refer others.
  5. **Sell advertising space** — Monetise large user bases.


## Earning partner commissions

Earn commissions on trades and payments made through your apps. Learn more about the [commission plans](<https://deriv.com/partners>).

By combining markup with these strategies, you can effectively monetize your Deriv API applications.

[OAuth 2.0](</docs/intro/oauth/>)[Options Setup](</docs/options/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#0362736a2e707673736c7177436766716a752d606c6e>)

---

## Options Setup

*Fonte: https://developers.deriv.com/docs/options/*

[Options Setup](</docs>)

# Options Setup REST

Manage Options trading accounts and establish WebSocket connections for real-time trading.

## Overview

The Options Setup APIs allow you to create and manage Options trading accounts using REST endpoints. These endpoints handle account creation, balance management, and WebSocket authentication setup.

##### REST API

These are standard REST APIs using HTTP methods (GET, POST). All requests require the `Deriv-App-ID: YOUR_APP_ID` header and an `Authorization: Bearer YOUR_OAUTH_TOKEN` token for authenticated endpoints.

##### OpenAPI Specification

View the complete OpenAPI 3.1.0 specification for detailed schema definitions and examples:

[Production OpenAPI Spec →](</api-docs/production-openapi.json>)[Staging OpenAPI Spec →](</api-docs/staging-openapi.json>)

## Typical Workflow

  1. Create an Options trading account using `POST /trading/v1/options/accounts`.
  2. Request a WebSocket URL via `POST /trading/v1/options/accounts/{accountId}/otp`.
  3. Connect directly to the WebSocket URL returned.
  4. Start trading operations through the WebSocket connection.


## Available Endpoints

[Get All AccountsGetGet all Options trading accounts`/trading/v1/options/accounts`](</docs/options/get-accounts/>)[Create AccountPostCreate a new Options trading account`/trading/v1/options/accounts`](</docs/options/create-account/>)[Reset Demo Account BalancePostReset balance for Options trading demo account`/trading/v1/options/accounts/{account_id}/reset-demo-balance`](</docs/options/reset-demo-balance/>)[WebSocketsPostGet a WebSocket URL via OTP and connect for real-time Options trading`/trading/v1/options/accounts/{accountId}/otp`](</docs/options/websocket/>)[WebSocket Public EndpointGetWebSocket endpoint for Options Trading public data that does not require authentication`/trading/v1/options/ws/public`](</docs/options/ws-public/>)

## Authentication

All authenticated endpoints require the `Deriv-App-ID` header and an `Authorization: Bearer YOUR_AUTH_TOKEN` header.

### OAuth2 Scopes

Endpoint| Scope  
---|---  
`GET /trading/v1/options/accounts`| `trade`  
`POST /trading/v1/options/accounts`| `account_manage`  
`POST /.../reset-demo-balance`| `trade`  
`POST .../{accountId}/otp`| `trade`  
      
    
    1curl -X POST https://api.derivws.com/trading/v1/options/accounts \
    2  -H "Deriv-App-ID: YOUR_APP_ID" \
    3  -H "Authorization: Bearer YOUR_OAUTH_TOKEN" \
    4  -H "Content-Type: application/json" \
    5  -d '{"currency": "USD", "group": "row", "account_type": "demo"}'

## Response Status Codes

The API uses standard HTTP status codes to indicate success or failure:

2xx Success

**200 OK** — Request successful (existing account or OTP generated)

**201 Created** — New resource created successfully

4xx/5xx Errors

**400 Bad Request** — Invalid parameters or request body

**401 Unauthorized** — Invalid or missing authentication

**403 Forbidden** — Access denied

**404 Not Found** — Resource not found

**500 Internal Server Error** — Server-side error

**504 Gateway Timeout** — Upstream service timeout

## Error Response Format

All error responses follow a consistent structure with an errors array and metadata:
    
    
    1{
    2  "errors": [
    3    {
    4      "status": 400,
    5      "code": "ValidationError",
    6      "message": "currency field is required"
    7    }
    8  ],
    9  "meta": {
    10    "endpoint": "/accounts",
    11    "method": "POST",
    12    "timing": 23
    13  }
    14}

Error codes include: `ValidationError`, `FieldIsRequired`, `Unauthorized`, `UnauthorizedAccess`, `AccessDenied`, `AccountNotFound`, `BadInputRequest`, `RateLimit`, `InternalServerError`

[Markup](</docs/intro/markup/>)[Get Accounts](</docs/options/get-accounts/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#95f4e5fcb8e6e0e5e5fae7e1d5f1f0e7fce3bbf6faf8>)

---

## Get accounts

*Fonte: https://developers.deriv.com/docs/options/get-accounts/*

[Options Setup](</docs/options>)

# Get All Accounts

GetAuth required

Get all Options trading accounts

## Endpoint

Get`/trading/v1/options/accounts`

Base URL: `https://api.derivws.com`

Request SchemaResponse SchemaExamples

[](</schemas/get_accounts_request.schema.json>)

## Status Codes

200OK - Successfully retrieved all accounts

400Bad request - Invalid request parameters

401Unauthorized - Invalid or missing authentication

403Forbidden - Access denied

404Not found - Resource not found

504Gateway timeout - Upstream service timeout

## Error Responses

400Bad request
    
    
    {
      "errors": [
        {
          "status": 400,
          "code": "ValidationError",
          "message": "Invalid request parameters"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "GET",
        "timing": 45
      }
    }

401Unauthorized
    
    
    {
      "errors": [
        {
          "status": 401,
          "code": "Unauthorized",
          "message": "Invalid or missing authentication credentials"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "GET",
        "timing": 12
      }
    }

403Forbidden
    
    
    {
      "errors": [
        {
          "status": 403,
          "code": "AccessDenied",
          "message": "You do not have permission to access this resource"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "GET",
        "timing": 15
      }
    }

404Not found
    
    
    {
      "errors": [
        {
          "status": 404,
          "code": "AccountNotFound",
          "message": "Resource not found"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "GET",
        "timing": 32
      }
    }

##### Authentication Required

This endpoint requires the `Deriv-App-ID` header to identify your application. OAuth2 Scope: `trade`

## About get_accounts

The `get_accounts` endpoint get all Options trading accounts

### Account Information

This endpoint returns all Options trading accounts associated with your authentication credentials, including both demo and real accounts.

[Options Setup](</docs/options/>)[Create Account](</docs/options/create-account/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#6504150c48161015150a1711250100170c134b060a08>)

---

## Create account

*Fonte: https://developers.deriv.com/docs/options/create-account/*

[Options Setup](</docs/options>)

# Create Account

PostAuth required

Create a new Options trading account

## Endpoint

Post`/trading/v1/options/accounts`

Base URL: `https://api.derivws.com`

Request SchemaResponse SchemaExamples

[](</schemas/create_account_request.schema.json>)

## Status Codes

200OK - Account already exists with the same parameters
    
    
    {
      "data": {
        "account_id": "DOT90004580",
        "balance": 10000,
        "currency": "USD",
        "group": "row",
        "status": "active",
        "account_type": "demo"
      },
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 245
      }
    }

201Created - New account successfully created
    
    
    {
      "data": [
        {
          "account_id": "DOT90004580",
          "balance": 10000,
          "currency": "USD",
          "group": "row",
          "status": "active",
          "account_type": "demo"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 456
      }
    }

400Bad request - Invalid or missing parameters

401Unauthorized - Invalid or missing authentication

403Forbidden - Access denied

500Internal server error

504Gateway timeout - Upstream service timeout

## Error Responses

400Bad request
    
    
    {
      "errors": [
        {
          "status": 400,
          "code": "FieldIsRequired",
          "message": "currency field is required"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 23
      }
    }

401Unauthorized
    
    
    {
      "errors": [
        {
          "status": 401,
          "code": "Unauthorized",
          "message": "Invalid or missing authentication credentials"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 12
      }
    }

403Forbidden
    
    
    {
      "errors": [
        {
          "status": 403,
          "code": "AccessDenied",
          "message": "You do not have permission to access this resource"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 15
      }
    }

500Internal server error
    
    
    {
      "errors": [
        {
          "status": 500,
          "code": "InternalServerError",
          "message": "An internal error occurred"
        }
      ],
      "meta": {
        "endpoint": "/accounts",
        "method": "POST",
        "timing": 67
      }
    }

##### Authentication Required

This endpoint requires the `Deriv-App-ID` header to identify your application. OAuth2 Scope: `account_manage`

## About create_account

The `create_account` endpoint create a new Options trading account

### Response Status Codes

  * **200 OK:** Returned when an account with the same parameters already exists. The response contains a single account object in the `data` field.
  * **201 Created:** Returned when a new account is successfully created. The response contains an array of accounts in the `data` field.


### Account Types

  * **Demo Account:** Practice trading with virtual funds. Perfect for testing and learning.
  * **Real Account:** Trade with real money. Requires proper verification and funding.


### Currency Support

Currently, Options accounts support USD currency. More currencies may be added in the future.

### Request Parameters

All fields are required:

  * `currency`: Must be "USD"
  * `group`: Must be "row"
  * `account_type`: Either "demo" or "real"


[Get Accounts](</docs/options/get-accounts/>)[Reset Demo Balance](</docs/options/reset-demo-balance/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#1776677e3a64626767786563577372657e613974787a>)

---

## Reset demo balance

*Fonte: https://developers.deriv.com/docs/options/reset-demo-balance/*

[Options Setup](</docs/options>)

# Reset Demo Account Balance

PostAuth required

Reset balance for Options trading demo account

## Endpoint

Post`/trading/v1/options/accounts/{account_id}/reset-demo-balance`

Base URL: `https://api.derivws.com`

Request SchemaResponse SchemaExamples

[](</schemas/reset_demo_balance_request.schema.json>)

## Status Codes

200OK - Demo balance successfully reset to default amount

400Bad request - Invalid account ID or not a demo account

401Unauthorized - Invalid or missing authentication

403Forbidden - Access denied

404Not found - Account not found

500Internal server error

504Gateway timeout - Upstream service timeout

## Error Responses

400Bad request
    
    
    {
      "errors": [
        {
          "status": 400,
          "code": "ValidationError",
          "message": "Only demo accounts can have their balance reset"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/reset-demo-balance",
        "method": "POST",
        "timing": 34
      }
    }

401Unauthorized
    
    
    {
      "errors": [
        {
          "status": 401,
          "code": "Unauthorized",
          "message": "Invalid or missing authentication credentials"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/reset-demo-balance",
        "method": "POST",
        "timing": 12
      }
    }

403Forbidden
    
    
    {
      "errors": [
        {
          "status": 403,
          "code": "AccessDenied",
          "message": "You do not have permission to access this resource"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/reset-demo-balance",
        "method": "POST",
        "timing": 15
      }
    }

500Internal server error
    
    
    {
      "errors": [
        {
          "status": 500,
          "code": "InternalServerError",
          "message": "An internal error occurred"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/reset-demo-balance",
        "method": "POST",
        "timing": 67
      }
    }

##### Authentication Required

This endpoint requires the `Deriv-App-ID` header to identify your application. OAuth2 Scope: `trade`

## About reset_demo_balance

The `reset_demo_balance` endpoint reset balance for Options trading demo account

### Demo Accounts Only

This endpoint only works for demo accounts. Real accounts cannot have their balance reset via API.

### Default Balance

The balance is reset to the default amount of $10,000 USD for demo accounts.

[Create Account](</docs/options/create-account/>)[WebSockets](</docs/options/websocket/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#e1809188cc929491918e9395a18584938897cf828e8c>)

---

## WebSockets

*Fonte: https://developers.deriv.com/docs/options/websocket/*

[Options Setup](</docs/options>)

# WebSockets

PostAuth required

Get a WebSocket URL via OTP and connect for real-time Options trading

## Endpoint

Post`/trading/v1/options/accounts/{accountId}/otp`

Base URL: `https://api.derivws.com`

Request SchemaResponse SchemaExamples

[](</schemas/websocket_request.schema.json>)

## Status Codes

200OTP generated successfully — response contains the WebSocket URL

400Bad request - Invalid account ID

401Unauthorized - Invalid or missing Bearer token

500Internal server error

## Error Responses

400Bad request
    
    
    {
      "errors": [
        {
          "status": 400,
          "code": "ValidationError",
          "message": "Invalid account ID format"
        }
      ],
      "meta": {
        "endpoint": "/accounts/INVALID/otp",
        "method": "POST",
        "timing": 12
      }
    }

401Unauthorized
    
    
    {
      "errors": [
        {
          "status": 401,
          "code": "Unauthorized",
          "message": "Invalid or missing authentication credentials"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/otp",
        "method": "POST",
        "timing": 45
      }
    }

500Internal server error
    
    
    {
      "errors": [
        {
          "status": 500,
          "code": "InternalServerError",
          "message": "Failed to generate OTP"
        }
      ],
      "meta": {
        "endpoint": "/accounts/DOT90004580/otp",
        "method": "POST",
        "timing": 234
      }
    }

##### Authentication Required

This endpoint requires the `Deriv-App-ID` header to identify your application. OAuth2 Scope: `trade`

## About websocket

The `websocket` endpoint get a WebSocket URL via OTP and connect for real-time Options trading

### Step 1: Get your WebSocket URL

Call the OTP endpoint with your Bearer token. The response contains a ready-to-use WebSocket URL that already includes the one-time password as a query parameter.
    
    
    // Request OTP — returns a WebSocket URL
    const response = await fetch(
      'https://api.derivws.com/trading/v1/options/accounts/DOT90004580/otp',
      {
        method: 'POST',
        headers: {
          'Deriv-App-ID': 'YOUR_APP_ID',
          'Authorization': 'Bearer YOUR_AUTH_TOKEN',
        },
      }
    );
    const { data } = await response.json();
    // data.url === "wss://api.derivws.com/trading/v1/options/ws/demo?otp=abc123xyz789"

### Step 2: Connect to the WebSocket

Use the URL returned in the previous step directly as your WebSocket endpoint. No additional authentication headers are needed — the OTP in the URL handles authentication.
    
    
    // Connect using the URL from the OTP response
    const ws = new WebSocket(data.url);
    
    ws.onopen = () => {
      console.log('Connected to Options trading WebSocket');
    };
    
    ws.onmessage = (event) => {
      const message = JSON.parse(event.data);
      console.log('Received:', message);
    };
    
    ws.onerror = (error) => {
      console.error('WebSocket error:', error);
    };

### OTP Lifetime

OTP tokens are short-lived and must be used immediately after generation. If the token expires before you connect, request a new one by calling the OTP endpoint again.

### Public WebSocket (no auth)

For read-only public market data, you can connect to `wss://api.derivws.com/trading/v1/options/ws/public` directly without any authentication or OTP.

[Reset Demo Balance](</docs/options/reset-demo-balance/>)[Account Management](</docs/account/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#9afbeaf3b7e9efeaeaf5e8eedafeffe8f3ecb4f9f5f7>)

---

## Account Management

*Fonte: https://developers.deriv.com/docs/account/*

[Endpoints](</docs>)

# Account Management WebSocket

Authentication and account management

[BalanceAuth requiredGet the account's balance`balance`](</docs/account/balance/>)[PortfolioAuth requiredGet a list of all open positions for the authorized account.`portfolio`](</docs/account/portfolio/>)[Profit TableAuth requiredRetrieve a summary of account profit/loss.`profit_table`](</docs/account/profit-table/>)[StatementAuth requiredRetrieve account statement.`statement`](</docs/account/statement/>)[TransactionAuth requiredSubscribe to transaction notifications.`transaction`](</docs/account/transaction/>)

[WebSockets](</docs/options/websocket/>)[Balance](</docs/account/balance/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#58392831752b2d2828372a2c183c3d2a312e763b3735>)

---

## Balance

*Fonte: https://developers.deriv.com/docs/account/balance/*

[Account Management](</docs/account>)

# Balance

Auth required

Get the account's balance

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/balance_request.schema.json>)

##### Subscription

When `subscribe: 1` is sent, the server will push a new balance message whenever the account balance changes. Use the `forget` API with the subscription `id` to stop receiving updates.

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About balance

The `balance` endpoint get the account's balance

This is an account management endpoint. Use it to manage authentication, balances, and account information.

[Account Management](</docs/account/>)[Portfolio](</docs/account/portfolio/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#d6b7a6bffba5a3a6a6b9a4a296b2b3a4bfa0f8b5b9bb>)

---

## Portfolio

*Fonte: https://developers.deriv.com/docs/account/portfolio/*

[Account Management](</docs/account>)

# Portfolio

Auth required

Get a list of all open positions for the authorized account.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/portfolio_request.schema.json>)

##### Open positions only

This endpoint returns only open (active) contracts. For historical closed contracts, use the `profit_table` or `statement` endpoints.

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About portfolio

The `portfolio` endpoint get a list of all open positions for the authorized account.

This is an account management endpoint. Use it to manage authentication, balances, and account information.

[Balance](</docs/account/balance/>)[Profit Table](</docs/account/profit-table/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#7312031a5e000603031c0107331716011a055d101c1e>)

---

## Profit table

*Fonte: https://developers.deriv.com/docs/account/profit-table/*

[Account Management](</docs/account>)

# Profit Table

Auth required

Retrieve a summary of account profit/loss.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/profit_table_request.schema.json>)

##### Pagination

Use `limit` and `offset` together to paginate through results. The default limit is 50 with a maximum of 500 records per call.

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About profit_table

The `profit_table` endpoint retrieve a summary of account profit/loss.

This is an account management endpoint. Use it to manage authentication, balances, and account information.

[Portfolio](</docs/account/portfolio/>)[Statement](</docs/account/statement/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#7d1c0d14500e080d0d120f093d19180f140b531e1210>)

---

## Statement

*Fonte: https://developers.deriv.com/docs/account/statement/*

[Account Management](</docs/account>)

# Statement

Auth required

Retrieve account statement.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/statement_request.schema.json>)

##### Filtering by action type

Use the `action_type` field to narrow results to specific transaction types: `buy`, `sell`, `deposit`, or `withdrawal`.

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About statement

The `statement` endpoint retrieve account statement.

This is an account management endpoint. Use it to manage authentication, balances, and account information.

[Profit Table](</docs/account/profit-table/>)[Transaction](</docs/account/transaction/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#adccddc480ded8ddddc2dfd9edc9c8dfc4db83cec2c0>)

---

## Transaction

*Fonte: https://developers.deriv.com/docs/account/transaction/*

[Account Management](</docs/account>)

# Transaction

Auth required

Subscribe to transaction notifications.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/transaction_request.schema.json>)

##### Real-time updates

This endpoint requires `subscribe: 1` — it is a streaming endpoint and won't return any data without it. Each transaction on your account triggers a new message. Use `forget` with the subscription `id` to stop receiving updates.

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About transaction

The `transaction` endpoint subscribe to transaction notifications.

This is an account management endpoint. Use it to manage authentication, balances, and account information.

[Statement](</docs/account/statement/>)[Market Data](</docs/data/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#39584950144a4c4949564b4d795d5c4b504f175a5654>)

---

## Market Data

*Fonte: https://developers.deriv.com/docs/data/*

[Endpoints](</docs>)

# Market Data WebSocket

Market data and symbols

[Active SymbolsNo authRetrieve a list of all currently active symbols (underlying markets upon which contracts are available for trading).`active_symbols`](</docs/data/active-symbols/>)[Contracts For SymbolNo authGet available contracts for a specific underlying symbol.`contracts_for`](</docs/data/contracts-for/>)[Contracts ListNo authGet the list of all contract categories available for the trading platform.`contracts_list`](</docs/data/contracts-list/>)[Ticks StreamNo authSubscribe to tick stream for a specific symbol.`ticks`](</docs/data/ticks/>)[Ticks HistoryNo authGet historical tick data for a symbol.`ticks_history`](</docs/data/ticks-history/>)

[Transaction](</docs/account/transaction/>)[Active Symbols](</docs/data/active-symbols/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#b3d2c3da9ec0c6c3c3dcc1c7f3d7d6c1dac59dd0dcde>)

---

## Active symbols

*Fonte: https://developers.deriv.com/docs/data/active-symbols/*

[Market Data](</docs/data>)

# Active Symbols

No auth

Retrieve a list of all currently active symbols (underlying markets upon which contracts are available for trading).

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/active_symbols_request.schema.json>)

## About active_symbols

The `active_symbols` endpoint retrieve a list of all currently active symbols (underlying markets upon which contracts are available for trading).

This is a market data endpoint. Use it to retrieve symbols, contracts, ticks, and historical data. Most data endpoints support subscriptions for real-time updates.

[Market Data](</docs/data/>)[Contracts For](</docs/data/contracts-for/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#107160793d636560607f62645074756279663e737f7d>)

---

## Contracts for

*Fonte: https://developers.deriv.com/docs/data/contracts-for/*

[Market Data](</docs/data>)

# Contracts For Symbol

No auth

Get available contracts for a specific underlying symbol.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/contracts_for_request.schema.json>)

## About contracts_for

The `contracts_for` endpoint get available contracts for a specific underlying symbol.

This is a market data endpoint. Use it to retrieve symbols, contracts, ticks, and historical data. Most data endpoints support subscriptions for real-time updates.

[Active Symbols](</docs/data/active-symbols/>)[Contracts List](</docs/data/contracts-list/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#2647564f0b55535656495452664243544f500845494b>)

---

## Contracts list

*Fonte: https://developers.deriv.com/docs/data/contracts-list/*

[Market Data](</docs/data>)

# Contracts List

No auth

Get the list of all contract categories available for the trading platform.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/contracts_list_request.schema.json>)

## About contracts_list

The `contracts_list` endpoint get the list of all contract categories available for the trading platform.

This is a market data endpoint. Use it to retrieve symbols, contracts, ticks, and historical data. Most data endpoints support subscriptions for real-time updates.

[Contracts For](</docs/data/contracts-for/>)[Ticks](</docs/data/ticks/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#fb9a8b92d6888e8b8b94898fbb9f9e89928dd5989496>)

---

## Ticks

*Fonte: https://developers.deriv.com/docs/data/ticks/*

[Market Data](</docs/data>)

# Ticks Stream

No auth

Subscribe to tick stream for a specific symbol.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/ticks_request.schema.json>)

## About ticks

The `ticks` endpoint subscribe to tick stream for a specific symbol.

This is a market data endpoint. Use it to retrieve symbols, contracts, ticks, and historical data. Most data endpoints support subscriptions for real-time updates.

[Contracts List](</docs/data/contracts-list/>)[Ticks History](</docs/data/ticks-history/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#18796871356b6d6868776a6c587c7d6a716e367b7775>)

---

## Ticks history

*Fonte: https://developers.deriv.com/docs/data/ticks-history/*

[Market Data](</docs/data>)

# Ticks History

No auth

Get historical tick data for a symbol.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/ticks_history_request.schema.json>)

## About ticks_history

The `ticks_history` endpoint get historical tick data for a symbol.

This is a market data endpoint. Use it to retrieve symbols, contracts, ticks, and historical data. Most data endpoints support subscriptions for real-time updates.

[Ticks](</docs/data/ticks/>)[Trading Operations](</docs/trading/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#6b0a1b0246181e1b1b04191f2b0f0e19021d45080406>)

---

## Trading Operations

*Fonte: https://developers.deriv.com/docs/trading/*

[Endpoints](</docs>)

# Trading Operations WebSocket

Buy, sell, and manage contracts

[Price ProposalNo authGet a price proposal for a specific contract.`proposal`](</docs/trading/proposal/>)[Buy ContractAuth requiredBuy a contract using a proposal ID.`buy`](</docs/trading/buy/>)[Sell ContractAuth requiredSell an open contract before expiry.`sell`](</docs/trading/sell/>)[Open Contract StatusNo authGet the latest status of an open contract.`proposal_open_contract`](</docs/trading/proposal-open-contract/>)[Update ContractAuth requiredUpdate settings for an open contract.`contract_update`](</docs/trading/contract-update/>)[Contract Update HistoryAuth requiredGet history of updates for a contract.`contract_update_history`](</docs/trading/contract-update-history/>)[Cancel ContractAuth requiredCancel a contract.`cancel`](</docs/trading/cancel/>)

[Ticks History](</docs/data/ticks-history/>)[Proposal](</docs/trading/proposal/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#fc9d8c95d18f898c8c938e88bc98998e958ad29f9391>)

---

## Proposal

*Fonte: https://developers.deriv.com/docs/trading/proposal/*

[Trading Operations](</docs/trading>)

# Price Proposal

No auth

Get a price proposal for a specific contract.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/proposal_request.schema.json>)

## About proposal

The `proposal` endpoint get a price proposal for a specific contract.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Trading Operations](</docs/trading/>)[Buy](</docs/trading/buy/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#8feeffe6a2fcfaffffe0fdfbcfebeafde6f9a1ece0e2>)

---

## Buy

*Fonte: https://developers.deriv.com/docs/trading/buy/*

[Trading Operations](</docs/trading>)

# Buy Contract

Auth required

Buy a contract using a proposal ID.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/buy_request.schema.json>)

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About buy

The `buy` endpoint buy a contract using a proposal ID.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Proposal](</docs/trading/proposal/>)[Sell](</docs/trading/sell/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#412031286c323431312e33350125243328376f222e2c>)

---

## Sell

*Fonte: https://developers.deriv.com/docs/trading/sell/*

[Trading Operations](</docs/trading>)

# Sell Contract

Auth required

Sell an open contract before expiry.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/sell_request.schema.json>)

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About sell

The `sell` endpoint sell an open contract before expiry.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Buy](</docs/trading/buy/>)[Proposal Open Contract](</docs/trading/proposal-open-contract/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#c4a5b4ade9b7b1b4b4abb6b084a0a1b6adb2eaa7aba9>)

---

## Proposal open contract

*Fonte: https://developers.deriv.com/docs/trading/proposal-open-contract/*

[Trading Operations](</docs/trading>)

# Open Contract Status

No auth

Get the latest status of an open contract.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/proposal_open_contract_request.schema.json>)

## About proposal_open_contract

The `proposal_open_contract` endpoint get the latest status of an open contract.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Sell](</docs/trading/sell/>)[Contract Update](</docs/trading/contract-update/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#a5c4d5cc88d6d0d5d5cad7d1e5c1c0d7ccd38bc6cac8>)

---

## Contract update

*Fonte: https://developers.deriv.com/docs/trading/contract-update/*

[Trading Operations](</docs/trading>)

# Update Contract

Auth required

Update settings for an open contract.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/contract_update_request.schema.json>)

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About contract_update

The `contract_update` endpoint update settings for an open contract.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Proposal Open Contract](</docs/trading/proposal-open-contract/>)[Contract Update History](</docs/trading/contract-update-history/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#0c6d7c65217f797c7c637e784c68697e657a226f6361>)

---

## Contract update history

*Fonte: https://developers.deriv.com/docs/trading/contract-update-history/*

[Trading Operations](</docs/trading>)

# Contract Update History

Auth required

Get history of updates for a contract.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/contract_update_history_request.schema.json>)

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About contract_update_history

The `contract_update_history` endpoint get history of updates for a contract.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Contract Update](</docs/trading/contract-update/>)[Cancel](</docs/trading/cancel/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#bfdecfd692cccacfcfd0cdcbffdbdacdd6c991dcd0d2>)

---

## Cancel

*Fonte: https://developers.deriv.com/docs/trading/cancel/*

[Trading Operations](</docs/trading>)

# Cancel Contract

Auth required

Cancel a contract.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/cancel_request.schema.json>)

##### Authentication Required

This endpoint requires a valid session. Ensure your WebSocket connection is authenticated before calling this endpoint.

## About cancel

The `cancel` endpoint cancel a contract.

This is a trading endpoint. Use it to get proposals, buy/sell contracts, and manage active positions. Trading endpoints require authentication.

[Contract Update History](</docs/trading/contract-update-history/>)[Subscription Management](</docs/subscription/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#b6d7c6df9bc5c3c6c6d9c4c2f6d2d3c4dfc098d5d9db>)

---

## Subscription Management

*Fonte: https://developers.deriv.com/docs/subscription/*

[Endpoints](</docs>)

# Subscription Management WebSocket

Manage WebSocket subscriptions

[Forget SubscriptionNo authUnsubscribe from a specific subscription.`forget`](</docs/subscription/forget/>)[Forget All SubscriptionsNo authUnsubscribe from all subscriptions of a specific type.`forget_all`](</docs/subscription/forget-all/>)

[Cancel](</docs/trading/cancel/>)[Forget](</docs/subscription/forget/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#c9a8b9a0e4babcb9b9a6bbbd89adacbba0bfe7aaa6a4>)

---

## Forget

*Fonte: https://developers.deriv.com/docs/subscription/forget/*

[Subscription Management](</docs/subscription>)

# Forget Subscription

No auth

Unsubscribe from a specific subscription.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/forget_request.schema.json>)

##### Where to get the subscription ID

The subscription ID is returned in the `subscription.id` field of any streaming response (e.g. `ticks`, `balance`, `proposal`). A response of `1` means the stream was successfully cancelled; `0` means no stream with that ID was found.

## About forget

The `forget` endpoint unsubscribe from a specific subscription.

This endpoint helps manage WebSocket subscriptions. Use it to unsubscribe from real-time data streams when no longer needed.

[Subscription Management](</docs/subscription/>)[Forget All](</docs/subscription/forget-all/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#2b4a5b4206585e5b5b44595f6b4f4e59425d05484446>)

---

## Forget all

*Fonte: https://developers.deriv.com/docs/subscription/forget-all/*

[Subscription Management](</docs/subscription>)

# Forget All Subscriptions

No auth

Unsubscribe from all subscriptions of a specific type.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/forget_all_request.schema.json>)

##### When to use forget_all vs forget

Use `forget_all` to bulk-cancel all streams of a given type — useful when navigating away from a screen. Use `forget` with a specific ID when you only want to cancel one particular subscription and keep others of the same type running.

## About forget_all

The `forget_all` endpoint unsubscribe from all subscriptions of a specific type.

This endpoint helps manage WebSocket subscriptions. Use it to unsubscribe from real-time data streams when no longer needed.

[Forget](</docs/subscription/forget/>)[System](</docs/system/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#a4c5d4cd89d7d1d4d4cbd6d0e4c0c1d6cdd28ac7cbc9>)

---

## System

*Fonte: https://developers.deriv.com/docs/system/*

[System](</docs>)

# System

System endpoints provide essential utilities for WebSocket connectivity checks, server time synchronisation, trading schedule information, and session management.

##### WebSocket & REST Endpoints

This category includes both WebSocket endpoints (ping, time, trading_times) and REST endpoints (health). All endpoints are public and do not require authentication.

## Available Endpoints

[PingWebSocketPing the server to check connectivity.](</docs/system/ping/>)[Server TimeWebSocketGet the current server time.](</docs/system/time/>)[Trading TimesWebSocketGet trading times for all symbols.](</docs/system/trading-times/>)[Health CheckRESTHealth check endpoint to verify service availability](</docs/system/health/>)

## Use Cases

  * **Connection Health Checks** — Use the ping endpoint to verify WebSocket connectivity and measure latency.
  * **Time Synchronisation** — Get accurate server time to synchronise your application's clock.
  * **Trading Schedule** — Check trading times to know when markets are open.


[Forget All](</docs/subscription/forget-all/>)[Ping](</docs/system/ping/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#e485948dc9979194948b9690a48081968d92ca878b89>)

---

## Ping

*Fonte: https://developers.deriv.com/docs/system/ping/*

[System](</docs/system>)

# Ping

No auth

Ping the server to check connectivity.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/ping_request.schema.json>)

##### Keeping connections alive

WebSocket connections may be closed by proxies or firewalls after a period of inactivity. Send a ping every 30–60 seconds to keep the connection open. You can use `req_id` to match each ping to its pong response.

## About ping

The `ping` endpoint ping the server to check connectivity.

This is a system endpoint. Use it for connectivity checks, clock synchronisation, and market schedule queries.

[System](</docs/system/>)[Server Time](</docs/system/time/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#cbaabba2e6b8bebbbba4b9bf8bafaeb9a2bde5a8a4a6>)

---

## Time

*Fonte: https://developers.deriv.com/docs/system/time/*

[System](</docs/system>)

# Server Time

No auth

Get the current server time.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/time_request.schema.json>)

##### Clock synchronisation

The server returns Unix epoch time in seconds. Use this to calculate the offset between your local clock and the server clock — important for expiry time calculations and time-sensitive contract operations.

## About time

The `time` endpoint get the current server time.

This is a system endpoint. Use it for connectivity checks, clock synchronisation, and market schedule queries.

[Ping](</docs/system/ping/>)[Trading Times](</docs/system/trading-times/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#8aebfae3a7f9fffafae5f8fecaeeeff8e3fca4e9e5e7>)

---

## Trading times

*Fonte: https://developers.deriv.com/docs/system/trading-times/*

[System](</docs/system>)

# Trading Times

No auth

Get trading times for all symbols.

## Request & Response

Request SchemaResponse SchemaExamples

[](</schemas/trading_times_request.schema.json>)

##### Response structure

The response is hierarchical: each market contains submarkets, and each submarket contains symbol entries with their open/close `times`, active `trading_days`, and any special `events`. Use this to show users when specific markets or instruments are available for trading.

## About trading_times

The `trading_times` endpoint get trading times for all symbols.

This is a system endpoint. Use it for connectivity checks, clock synchronisation, and market schedule queries.

[Server Time](</docs/system/time/>)[Health Check](</docs/system/health/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#214051480c525451514e53556145445348570f424e4c>)

---

## Health check

*Fonte: https://developers.deriv.com/docs/system/health/*

[System](</docs/system>)

# Health Check

GETNo auth

Health check endpoint to verify service availability

## Endpoint

GET`/v1/health`

Base URL: `https://api.derivws.com`

##### No Authentication Required

This endpoint is publicly accessible and does not require any authentication headers.

Request SchemaResponse SchemaExamples

[](</schemas/health_request.schema.json>)

## Status Codes

200OK - Service is healthy and operational
    
    
    OK

503Service Unavailable - Service is down or experiencing issues

## Error Responses

When the service is unavailable or experiencing issues, the endpoint may return error responses.

503Service Unavailable
    
    
    Service temporarily unavailable

## About Health Checks

The health check endpoint provides a simple way to verify that the Deriv API service is operational and responding to requests.

### Response Format

Returns plain text `OK` when the service is healthy. A non-200 status code indicates the service is experiencing issues.

## Best Practices

  * Poll at regular intervals (e.g. every 30–60 seconds) for continuous monitoring
  * Set appropriate timeouts (e.g. 5 seconds) to detect slow responses
  * Monitor both response status and response time
  * Log health check failures with timestamps for troubleshooting
  * Implement exponential backoff if health checks fail repeatedly


## Example Usage

cURLJavaScriptPython
    
    
    curl https://api.derivws.com/v1/health

## Monitoring Integration

Uptime Monitors

Use services like UptimeRobot, Pingdom, or StatusCake to monitor this endpoint:

  * Set check interval to 1–5 minutes
  * Alert on HTTP status code != 200
  * Alert on response time > 2 seconds


Application Monitoring (APM)

Integrate with APM tools like DataDog, New Relic, or Prometheus:

  * Track response times as metrics
  * Set up alerts for failures
  * Create dashboards for uptime visualization


[Trading Times](</docs/system/trading-times/>)[Workflows](</docs/workflows/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#d5b4a5bcf8a6a0a5a5baa7a195b1b0a7bca3fbb6bab8>)

---

## Workflows

*Fonte: https://developers.deriv.com/docs/workflows/*

[Advanced](</docs>)

# Complete Workflows

End-to-end examples of common trading workflows using the Deriv API

## Prerequisites

##### Important: Complete These Steps First

You must complete all prerequisites before making any API calls. Skipping these steps will result in `401 Unauthorized` errors.

  1. **Log in to developers.deriv.com:** Create an account or log in with your credentials to access the dashboard.
  2. **Register a new application:** Navigate to the Dashboard and register a new application under your account. Choose the appropriate application type based on your use case:
     * **PAT type:** Choose this when browser redirects are not practical and manual token entry is acceptable. For example, desktop tools, CLI apps, or native clients. The user generates a Personal Access Token in Deriv and pastes it into your app.
     * **OAuth type:** Choose this when your product can handle browser redirects and you need a standard delegated flow with user authorisation. For example, web dashboards or browser apps. OAuth 2.0 issues short-lived tokens and minimises long-term credential sharing.

This will generate a new App ID. Your legacy App IDs will not work with the new APIs.

  3. **Generate an authorization token:**
     * **If using PAT type:** In the Dashboard, go to the API tokens section. Create a new PAT (Personal Access Token) and select the appropriate scopes (e.g., `trade`, `account_manage`). Copy and securely store your token. It cannot be viewed again after creation.
     * **If using OAuth type:** You do not need to generate a token manually. Proceed to the OAuth 2.0 authentication flow described below. The flow will provide a short-lived access token after successful authentication. Ensure you have your `client_id`, `client_secret`, and an HTTPS `redirect_uri` registered.
  4. **Configure your request headers:** Every REST API request must include both required headers:


required-headers.jsjavascript
    
    
    1// Required headers for ALL REST API calls
    2headers: {
    3  'Authorization': 'Bearer YOUR_AUTHORIZATION_TOKEN',  // Authorization token (PAT or JWT)
    4  'Deriv-App-ID': 'YOUR_APP_ID',                      // App ID from your registered application
    5  'Content-Type': 'application/json'
    6}

##### Authorization Token

Throughout this documentation, "authorization token" refers to either your **PAT token** (Personal Access Token) or your **JWT token** (obtained via the OAuth 2.0 flow), depending on which authentication method you are using. Both token types work the same way: as a Bearer token in the `Authorization` header for REST API calls, and to obtain an authenticated WebSocket URL via the OTP endpoint.

## Options Trading Workflow (REST + WebSocket)

##### Complete Integration

Use REST APIs for account setup and the OTP endpoint to get an authenticated WebSocket URL for real-time trading.

  1. **REST:** Get an authenticated WebSocket URL via the OTP endpoint (requires your authorization token)
  2. **WebSocket:** Connect using the authenticated URL from the OTP response
  3. **WebSocket:** Perform trading operations


**Note:** Users receive a default demo account upon signup. You do not need to create an account via the API before trading.

### Step 1: Get Authenticated WebSocket URL (REST)

This REST call requires your authorization token (PAT or JWT). The response contains an authenticated WebSocket URL that you can connect to directly. The URL determines whether you connect to a demo or real endpoint based on the account ID provided.

get-otp.jsjavascript
    
    
    1// Get authenticated WebSocket URL via OTP endpoint
    2// Note: This REST call requires your authorization token
    3const otpResponse = await fetch(
    4  `https://api.derivws.com/trading/v1/options/accounts/${accountId}/otp`,
    5  {
    6    method: 'POST',
    7    headers: {
    8      'Authorization': 'Bearer YOUR_AUTHORIZATION_TOKEN',  // PAT or JWT token
    9      'Deriv-App-ID': 'YOUR_APP_ID'
    10    }
    11  }
    12);
    13
    14const otpResult = await otpResponse.json();
    15const wsUrl = otpResult.data.url;
    16console.log('Authenticated WebSocket URL:', wsUrl);
    17// Output: wss://api.derivws.com/trading/v1/options/ws/demo?otp=abc123xyz789

### Step 2: Connect to WebSocket

**WebSocket Endpoints:** There are three WebSocket endpoints available:

  * **Public:** No authentication required. For market data and public information.
  * **Demo:** Authenticated. For demo account trading.
  * **Real:** Authenticated. For live account trading.

If the user is not logged in, use the public endpoint. Once authenticated, the OTP response URL will automatically point to the correct demo or real endpoint based on the account ID.

connect-websocket.jsjavascript
    
    
    1// Connect to Options WebSocket using the authenticated URL from OTP response
    2// The URL already contains the correct endpoint (demo/real) and authentication
    3const ws = new WebSocket(wsUrl);
    4
    5ws.onopen = () => {
    6  console.log('Connected to Options trading WebSocket');
    7  // Connection is now authenticated and ready for trading
    8};
    9
    10ws.onmessage = (msg) => {
    11  const data = JSON.parse(msg.data);
    12  console.log('Received:', data);
    13};
    14
    15ws.onerror = (error) => {
    16  console.error('WebSocket error:', error);
    17};
    18
    19ws.onclose = () => {
    20  console.log('WebSocket connection closed');
    21};

### Step 3: Start Trading Operations

trading-operations.jsjavascript
    
    
    1// Once connected, you can send trading commands through WebSocket
    2// Example: Get account balance
    3ws.send(JSON.stringify({
    4  balance: 1,
    5  subscribe: 1,
    6  req_id: 1
    7}));
    8
    9// Example: Subscribe to tick stream
    10ws.send(JSON.stringify({
    11  ticks: "1HZ100V",
    12  subscribe: 1,
    13  req_id: 2
    14}));
    15
    16// Example: Get price proposal
    17ws.send(JSON.stringify({
    18  proposal: 1,
    19  amount: 10,
    20  basis: "stake",
    21  contract_type: "MULTDOWN",
    22  currency: "USD",
    23  duration_unit: "s",
    24  multiplier: 10,
    25  underlying_symbol: "1HZ100V",
    26  subscribe: 1,
    27  req_id: 3
    28}));

##### Complete Example

Complete example combining all steps:

complete-workflow.jsjavascript
    
    
    1async function setupOptionsTrading() {
    2  const AUTH_TOKEN = 'YOUR_AUTHORIZATION_TOKEN';  // PAT or JWT token
    3  const APP_ID = 'YOUR_APP_ID';                   // App ID from registered application
    4  const API_BASE = 'https://api.derivws.com';
    5  const accountId = 'YOUR_ACCOUNT_ID';            // Your demo or real account ID
    6
    7  try {
    8    // Step 1: Get authenticated WebSocket URL (REST, requires authorization token)
    9    const otpResponse = await fetch(
    10      `${API_BASE}/trading/v1/options/accounts/${accountId}/otp`,
    11      {
    12        method: 'POST',
    13        headers: {
    14          'Authorization': `Bearer ${AUTH_TOKEN}`,
    15          'Deriv-App-ID': APP_ID
    16        }
    17      }
    18    );
    19
    20    if (!otpResponse.ok) throw new Error(`HTTP error! status: ${otpResponse.status}`);
    21    const otpData = await otpResponse.json();
    22    const wsUrl = otpData.data.url;
    23    console.log('✓ Authenticated WebSocket URL obtained');
    24
    25    // Step 2: Connect to WebSocket using the authenticated URL
    26    const ws = new WebSocket(wsUrl);
    27
    28    ws.onopen = () => {
    29      console.log('✓ WebSocket connected');
    30
    31      // Step 3: Start trading
    32      // Subscribe to balance updates
    33      ws.send(JSON.stringify({
    34        balance: 1,
    35        subscribe: 1,
    36        req_id: 1
    37      }));
    38
    39      // Subscribe to ticks
    40      ws.send(JSON.stringify({
    41        ticks: "1HZ100V",
    42        subscribe: 1,
    43        req_id: 2
    44      }));
    45    };
    46
    47    ws.onmessage = (msg) => {
    48      const data = JSON.parse(msg.data);
    49
    50      if (data.msg_type === 'balance') {
    51        console.log('Balance:', data.balance.balance, data.balance.currency);
    52      }
    53
    54      if (data.msg_type === 'tick') {
    55        console.log('Tick:', data.tick.quote);
    56      }
    57    };
    58
    59    return ws;
    60
    61  } catch (error) {
    62    console.error('Setup failed:', error);
    63    throw error;
    64  }
    65}
    66
    67// Run the setup
    68setupOptionsTrading().then(ws => {
    69  console.log('Trading setup complete. WebSocket ready for operations.');
    70}).catch(err => {
    71  console.error('Failed to setup trading:', err);
    72});

## Authentication Workflows

### Workflow A: PAT-Based Authentication

With a PAT app, the user generates a Personal Access Token in Deriv and manually enters or pastes it into your application. The app securely stores the token and includes it in API requests as a bearer token. This is best suited for desktop tools, CLI apps, and native clients where browser redirects are not practical.

  1. Log in to `developers.deriv.com` with your credentials
  2. Register a new application with **PAT type** in the Dashboard
  3. Generate a PAT token with appropriate scopes
  4. Include `Authorization: Bearer <YOUR_AUTHORIZATION_TOKEN>` and `Deriv-App-ID` in all REST request headers
  5. Make authenticated REST API calls


pat-authentication.jsjavascript
    
    
    1// PAT-Based Authentication: REST API
    2const AUTH_TOKEN = 'YOUR_AUTHORIZATION_TOKEN';  // Your PAT token
    3const APP_ID = 'YOUR_APP_ID';
    4
    5// All REST calls use the authorization token as a Bearer token
    6const response = await fetch('https://api.derivws.com/trading/v1/options/accounts', {
    7  method: 'POST',
    8  headers: {
    9    'Authorization': `Bearer ${AUTH_TOKEN}`,    // Authorization token (PAT)
    10    'Deriv-App-ID': APP_ID,                      // App ID from registered application
    11    'Content-Type': 'application/json'
    12  },
    13  body: JSON.stringify({
    14    currency: 'USD',
    15    group: 'row',
    16    account_type: 'demo'
    17  })
    18});
    19
    20const result = await response.json();
    21console.log('Authenticated REST call successful:', result);

### Workflow B: OAuth 2.0 Authentication

OAuth 2.0 lets users grant your app access without sharing their password. Your app redirects the user to a Deriv sign-in and consent page. After the user logs in and approves permissions, Deriv returns an authorization code to your app. You exchange this code for an access token, which you then use to authenticate API requests. Recommended for web-based applications onboarding end users.

#### Before You Begin

  * Ensure your redirect URL is correctly registered in the dashboard
  * The redirect URL must use HTTPS
  * Your app must handle redirects, read the authorization code, and exchange it for tokens
  * You must have a registered OAuth 2.0 client with valid credentials: `client_id`, `client_secret`, and `redirect_uri`
  * All redirect URLs (including subdirectories) must be whitelisted. URLs must match exactly.


##### Important: Redirect URL Whitelisting

When registering your OAuth application, you must define all redirect URLs in the dashboard. If you have given a URL like `https://abc.com` but your redirect URL is `https://abc.com/callback`, the flow will fail. Every subdirectory must be registered separately.

#### OAuth 2.0 Flow Steps

  1. Your app redirects the user to Deriv's OAuth 2.0 authorization page to sign in and review permissions
  2. The authorization server handles login and consent securely
  3. After login, Deriv redirects the user back to your app with an **authorization code** and **state** parameter
  4. Your app exchanges this code for tokens (with PKCE if used)
  5. The OAuth server returns the **access token** (and optional refresh token)
  6. Your app securely stores and uses the access token for WebSocket or REST API calls


oauth-authentication.jsjavascript
    
    
    1// OAuth 2.0 Authentication Flow (Authorization Code with PKCE)
    2const CLIENT_ID = 'YOUR_CLIENT_ID';
    3const REDIRECT_URI = 'https://your-app.com/callback';
    4
    5// --- PKCE Helper Functions ---
    6function generateCodeVerifier() {
    7  const array = new Uint8Array(32);
    8  crypto.getRandomValues(array);
    9  return btoa(String.fromCharCode(...array))
    10    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    11}
    12
    13async function generateCodeChallenge(verifier) {
    14  const encoder = new TextEncoder();
    15  const data = encoder.encode(verifier);
    16  const digest = await crypto.subtle.digest('SHA-256', data);
    17  return btoa(String.fromCharCode(...new Uint8Array(digest)))
    18    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    19}
    20
    21// Step 1: Generate PKCE values and redirect user to authorization endpoint
    22const codeVerifier = generateCodeVerifier();
    23const codeChallenge = await generateCodeChallenge(codeVerifier);
    24const state = crypto.randomUUID();
    25
    26sessionStorage.setItem('code_verifier', codeVerifier);
    27sessionStorage.setItem('oauth_state', state);
    28
    29const authUrl = new URL('https://auth.deriv.com/oauth2/auth');
    30authUrl.searchParams.set('response_type', 'code');
    31authUrl.searchParams.set('client_id', CLIENT_ID);
    32authUrl.searchParams.set('redirect_uri', REDIRECT_URI);
    33authUrl.searchParams.set('scope', 'trade account_manage');
    34authUrl.searchParams.set('state', state);
    35authUrl.searchParams.set('code_challenge', codeChallenge);
    36authUrl.searchParams.set('code_challenge_method', 'S256');
    37
    38window.location.href = authUrl.toString();
    39
    40// Step 3: Handle the callback
    41const urlParams = new URLSearchParams(window.location.search);
    42const authorizationCode = urlParams.get('code');
    43const returnedState = urlParams.get('state');
    44
    45const savedState = sessionStorage.getItem('oauth_state');
    46if (returnedState !== savedState) {
    47  throw new Error('State mismatch: possible CSRF attack');
    48}
    49
    50// Step 4: Exchange the authorization code for tokens
    51const savedVerifier = sessionStorage.getItem('code_verifier');
    52const tokenResponse = await fetch('https://auth.deriv.com/oauth2/token', {
    53  method: 'POST',
    54  headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    55  body: new URLSearchParams({
    56    grant_type: 'authorization_code',
    57    client_id: CLIENT_ID,
    58    code: authorizationCode,
    59    redirect_uri: REDIRECT_URI,
    60    code_verifier: savedVerifier
    61  })
    62});
    63
    64const tokenData = await tokenResponse.json();
    65const accessToken = tokenData.access_token;
    66console.log('Access token obtained, expires in', tokenData.expires_in, 'seconds');
    67
    68// Step 6: Use the access token for authenticated API calls
    69const response = await fetch('https://api.derivws.com/trading/v1/options/accounts', {
    70  method: 'GET',
    71  headers: {
    72    'Authorization': `Bearer ${accessToken}`,
    73    'Deriv-App-ID': CLIENT_ID,
    74    'Content-Type': 'application/json'
    75  }
    76});
    77
    78const result = await response.json();
    79console.log('Authenticated API call successful:', result);

##### Security Best Practices

  * Always validate the `state` parameter to prevent CSRF attacks
  * Generate your `code_challenge` from a cryptographically secure random `code_verifier`
  * Store tokens securely on the server. Never expose them in frontend code.
  * Access tokens are short-lived (typically 3600 seconds) and must be refreshed
  * The authorization code is single-use and short-lived


### WebSocket Authentication (OTP)

To connect to an authenticated WebSocket endpoint, you need to call the OTP REST endpoint using your authorization token (PAT or JWT). The response contains an authenticated WebSocket URL that you can connect to directly. The URL handles the authentication for you.

#### WebSocket Endpoints:

Public

No authentication required

Use for market data and public information. No login needed.

Demo

Authenticated

Use for demo/virtual account trading operations. The OTP response URL will point to this endpoint for demo accounts.

Real

Authenticated

Use for live/real account trading. The OTP response URL will point to this endpoint for real accounts.

websocket-otp-auth.jsjavascript
    
    
    1// Step 1: Get authenticated WebSocket URL via REST (requires authorization token)
    2const otpResponse = await fetch(
    3  `https://api.derivws.com/trading/v1/options/accounts/${accountId}/otp`,
    4  {
    5    method: 'POST',
    6    headers: {
    7      'Authorization': 'Bearer YOUR_AUTHORIZATION_TOKEN',  // PAT or JWT token
    8      'Deriv-App-ID': 'YOUR_APP_ID'
    9    }
    10  }
    11);
    12
    13const otpResult = await otpResponse.json();
    14const wsUrl = otpResult.data.url;
    15// The URL already includes the correct endpoint and authentication
    16// Output: wss://api.derivws.com/trading/v1/options/ws/demo?otp=abc123xyz789
    17
    18// Step 2: Connect to WebSocket using the authenticated URL
    19const ws = new WebSocket(wsUrl);
    20
    21ws.onopen = () => {
    22  console.log('WebSocket authenticated and connected');
    23  // Ready for trading operations
    24};

## Complete Trading Workflow

##### End-to-End Trading Process

This workflow shows how to authenticate, get market data, create a proposal, buy a contract, and monitor it.

  1. Establish connection and authenticate
  2. Get active symbols using `active_symbols`
  3. Subscribe to tick stream for chosen symbol using `ticks`
  4. Get contract proposal using `proposal` (with subscribe)
  5. Monitor real-time price updates
  6. When ready, buy contract using `buy`
  7. Subscribe to contract updates using `proposal_open_contract`
  8. Monitor contract status in real-time
  9. Optionally sell early using `sell`
  10. Check portfolio using `portfolio`


trading-workflow.jsjavascript
    
    
    1// After authentication...
    2
    3// 1. Get active symbols
    4ws.send(JSON.stringify({
    5  active_symbols: "brief",
    6  req_id: 3
    7}));
    8
    9// 2. Subscribe to ticks
    10ws.send(JSON.stringify({
    11  ticks: "1HZ100V",
    12  subscribe: 1,
    13  req_id: 4
    14}));
    15
    16// 3. Get price proposal
    17ws.send(JSON.stringify({
    18  proposal: 1,
    19  amount: 10,
    20  basis: "stake",
    21  contract_type: "MULTDOWN",
    22  currency: "USD",
    23  duration_unit: "s",
    24  multiplier: 10,
    25  underlying_symbol: "1HZ100V",
    26  subscribe: 1,
    27  req_id: 5
    28}));
    29
    30// 4. Buy the contract (when ready)
    31// Use proposal ID from previous response
    32ws.send(JSON.stringify({
    33  buy: "PROPOSAL_ID_HERE",
    34  price: 100,
    35  req_id: 6
    36}));
    37
    38// 5. Monitor contract status
    39ws.send(JSON.stringify({
    40  proposal_open_contract: 1,
    41  contract_id: CONTRACT_ID,
    42  subscribe: 1,
    43  req_id: 7
    44}));

## Market Data Workflow

##### No Authentication Required

You can access market data using the **public** WebSocket endpoint without authentication. Perfect for building chart applications or market monitors.

  1. Connect to the public WebSocket endpoint (no auth needed)
  2. Request `active_symbols` to see available markets
  3. Subscribe to `ticks` for real-time price updates
  4. Optionally get `ticks_history` for historical data
  5. Use `contracts_for` to see available contract types
  6. Stream continues until `forget` or disconnect


market-data.jsjavascript
    
    
    1// Connect to the public WebSocket endpoint (no authentication required)
    2const ws = new WebSocket('wss://ws.binaryws.com/websockets/v3');
    3
    4ws.onopen = () => {
    5  // Get available symbols
    6  ws.send(JSON.stringify({
    7    active_symbols: "brief",
    8    product_type: "basic",
    9    req_id: 1
    10  }));
    11
    12  // Subscribe to tick stream
    13  ws.send(JSON.stringify({
    14    ticks: "1HZ100V",
    15    subscribe: 1,
    16    req_id: 2
    17  }));
    18
    19  // Get historical data
    20  ws.send(JSON.stringify({
    21    ticks_history: "1HZ100V",
    22    count: 100,
    23    end: "latest",
    24    style: "ticks",
    25    req_id: 3
    26  }));
    27};
    28
    29ws.onmessage = (msg) => {
    30  const data = JSON.parse(msg.data);
    31
    32  if (data.msg_type === 'active_symbols') {
    33    console.log('Available symbols:', data.active_symbols);
    34  }
    35
    36  if (data.msg_type === 'tick') {
    37    console.log('Current price:', data.tick.quote);
    38  }
    39
    40  if (data.msg_type === 'history') {
    41    console.log('Historical data:', data.history);
    42  }
    43};

## Troubleshooting

401 Unauthorized

"You are not authorised to access this resource"

Common causes:

  * Missing Authorization: Bearer <YOUR_AUTHORIZATION_TOKEN> header in REST requests
  * Using an expired or invalid authorization token
  * Using a legacy App ID instead of a new App ID registered on developers.deriv.com
  * Mismatched application type, such as using a PAT token with an OAuth-type application, or vice versa


**Solution:** Ensure your REST requests include Authorization: Bearer YOUR_AUTHORIZATION_TOKEN and use a new App ID registered on developers.deriv.com. Make sure your token type matches your application type.

403 Forbidden

Insufficient permissions

Common causes:

  * Authorization token does not have the required scopes for the endpoint
  * You created the token without trade or account_manage scope


**Solution:** Regenerate your token with the correct scopes selected. At least one scope must be defined when creating a token.

Invalid App ID

App ID not recognised

Common causes:

  * Using a legacy App ID with the new API
  * Using an App ID not registered on developers.deriv.com
  * Using an App ID with the wrong type


**Solution:** Log in to developers.deriv.com and register a new application with the correct type (PAT or OAuth) to get a new App ID.

Expired Access Token

Token no longer valid

Common causes:

  * OAuth 2.0 access tokens are short-lived (typically 3600 seconds / 1 hour) and expire automatically
  * PAT tokens can be revoked manually from the dashboard


**Solution:** For OAuth apps, implement token refresh logic using the refresh token. For PAT apps, generate a new token from the dashboard. Never store tokens in frontend code or expose them in URLs.

OAuth Redirect Failure

Consent flow fails or the app does not redirect the user

Common causes:

  * Redirect URL is not whitelisted in the application dashboard
  * Redirect URL includes subdirectories that were not registered
  * Mismatch between the URL used in the OAuth request and the URLs registered in the dashboard


**Solution:** Ensure all redirect URLs (including subdirectories) are registered in your application settings on developers.deriv.com. The URLs must match exactly.

## Common Patterns

Subscription Management

How to manage WebSocket subscriptions effectively

  * Always store subscription IDs
  * Use forget to unsubscribe
  * Use forget_all to clear all
  * Clean up subscriptions before disconnect


Error Handling

Best practices for handling API errors

  * Always check for error field
  * Implement exponential backoff for retries
  * Log errors with context
  * Handle network disconnections gracefully


Request IDs

How to track requests and responses

  * Use unique req_id for each request
  * Match responses using req_id
  * Helps with concurrent requests
  * Essential for debugging


Connection Lifecycle

Managing WebSocket connection state

  * Handle onopen, onclose, onerror
  * Implement auto-reconnect logic
  * Re-authenticate after reconnect
  * Restore subscriptions on reconnect


[Health Check](</docs/system/health/>)

Any other questions? [Get in touch](</cdn-cgi/l/email-protection#2342534a0e505653534c5157634746514a550d404c4e>)

---

