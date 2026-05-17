// WebAuthn / passkey client helpers.
//
// Two ceremonies, each two HTTP round-trips against /auth/passkey/...:
//
//   Registration (caller must already be signed in):
//     1. POST /auth/passkey/register/begin  -> CredentialCreateOptions JSON
//     2. navigator.credentials.create({ publicKey })
//     3. POST /auth/passkey/register/complete with the attestation
//
//   Login (anonymous):
//     1. POST /auth/passkey/login/begin     -> AssertionOptions JSON
//     2. navigator.credentials.get({ publicKey })
//     3. POST /auth/passkey/login/complete with the assertion
//
// The server stores the per-ceremony challenge in a short-lived session
// cookie so we don't need to round-trip it through the browser.

(function () {
    "use strict";

    /**
     * Decodes a base64url string (no padding, '-' and '_' instead of '+' and '/')
     * into an ArrayBuffer suitable for the WebAuthn API.
     * @param {string} value Base64url-encoded string.
     * @returns {ArrayBuffer}
     */
    function base64UrlToArrayBuffer(value) {
        const padded = value.replace(/-/g, "+").replace(/_/g, "/");
        const pad = padded.length % 4;
        const fullyPadded = pad === 0 ? padded : padded + "=".repeat(4 - pad);
        const binary = atob(fullyPadded);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes.buffer;
    }

    /**
     * Encodes an ArrayBuffer or TypedArray as base64url (no padding).
     * @param {ArrayBuffer|Uint8Array} buffer Source bytes.
     * @returns {string}
     */
    function arrayBufferToBase64Url(buffer) {
        const bytes = buffer instanceof ArrayBuffer ? new Uint8Array(buffer) : buffer;
        let binary = "";
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
    }

    /**
     * Converts the JSON options returned by /begin into the ArrayBuffer-typed
     * shape the WebAuthn browser API expects. Mutates a shallow copy so the
     * caller's object stays untouched.
     */
    function decodeCreateOptions(options) {
        return {
            ...options,
            challenge: base64UrlToArrayBuffer(options.challenge),
            user: {
                ...options.user,
                id: base64UrlToArrayBuffer(options.user.id),
            },
            excludeCredentials: (options.excludeCredentials || []).map(c => ({
                ...c,
                id: base64UrlToArrayBuffer(c.id),
            })),
        };
    }

    function decodeRequestOptions(options) {
        return {
            ...options,
            challenge: base64UrlToArrayBuffer(options.challenge),
            allowCredentials: (options.allowCredentials || []).map(c => ({
                ...c,
                id: base64UrlToArrayBuffer(c.id),
            })),
        };
    }

    /**
     * Serialises a PublicKeyCredential (attestation response) for the
     * /register/complete endpoint.
     */
    function encodeAttestation(credential) {
        return {
            id: credential.id,
            rawId: arrayBufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: arrayBufferToBase64Url(credential.response.attestationObject),
                clientDataJSON: arrayBufferToBase64Url(credential.response.clientDataJSON),
            },
            extensions: credential.getClientExtensionResults
                ? credential.getClientExtensionResults()
                : {},
        };
    }

    /**
     * Serialises a PublicKeyCredential (assertion response) for the
     * /login/complete endpoint.
     */
    function encodeAssertion(credential) {
        return {
            id: credential.id,
            rawId: arrayBufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                authenticatorData: arrayBufferToBase64Url(credential.response.authenticatorData),
                clientDataJSON: arrayBufferToBase64Url(credential.response.clientDataJSON),
                signature: arrayBufferToBase64Url(credential.response.signature),
                userHandle: credential.response.userHandle
                    ? arrayBufferToBase64Url(credential.response.userHandle)
                    : null,
            },
            extensions: credential.getClientExtensionResults
                ? credential.getClientExtensionResults()
                : {},
        };
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
            },
            body: JSON.stringify(body ?? {}),
        });
        const text = await response.text();
        let parsed = null;
        if (text) {
            try { parsed = JSON.parse(text); }
            catch { parsed = { raw: text }; }
        }
        return { ok: response.ok, status: response.status, body: parsed };
    }

    function ensureSupported() {
        if (!window.PublicKeyCredential || !navigator.credentials || !navigator.credentials.create) {
            throw new Error("WebAuthn is not supported in this browser.");
        }
    }

    /**
     * Registers a new passkey for the currently signed-in user.
     * @param {string} nickname Human-friendly label for the new credential.
     * @returns {Promise<{credentialId: string, nickname: string}>}
     */
    async function registerPasskey(nickname) {
        ensureSupported();

        const begin = await postJson("/auth/passkey/register/begin", {});
        if (!begin.ok) {
            throw new Error(begin.body?.error || `Failed to start registration (HTTP ${begin.status}).`);
        }

        const options = decodeCreateOptions(begin.body);
        const credential = await navigator.credentials.create({ publicKey: options });
        if (!credential) {
            throw new Error("The authenticator did not return a credential.");
        }

        const complete = await postJson("/auth/passkey/register/complete", {
            attestationResponse: encodeAttestation(credential),
            nickname: nickname || "",
        });
        if (!complete.ok) {
            throw new Error(complete.body?.error || `Failed to finish registration (HTTP ${complete.status}).`);
        }

        return complete.body;
    }

    /**
     * Signs the user in with a previously registered passkey.
     * @param {string} email Optional email to scope the challenge to.
     * @returns {Promise<{email: string}>}
     */
    async function loginWithPasskey(email) {
        ensureSupported();

        const begin = await postJson("/auth/passkey/login/begin", { email: email || null });
        if (!begin.ok) {
            throw new Error(begin.body?.error || `Failed to start sign-in (HTTP ${begin.status}).`);
        }

        const options = decodeRequestOptions(begin.body);
        const credential = await navigator.credentials.get({ publicKey: options });
        if (!credential) {
            throw new Error("The authenticator did not return an assertion.");
        }

        const complete = await postJson("/auth/passkey/login/complete", {
            assertionResponse: encodeAssertion(credential),
        });
        if (!complete.ok) {
            throw new Error(complete.body?.error || `Failed to finish sign-in (HTTP ${complete.status}).`);
        }

        return complete.body;
    }

    /**
     * Deletes a registered passkey owned by the current user.
     * @param {string} credentialId Base64url-encoded credential id to remove.
     * @returns {Promise<void>}
     */
    async function deletePasskey(credentialId) {
        const response = await fetch(`/auth/passkey/${encodeURIComponent(credentialId)}`, {
            method: "DELETE",
            credentials: "same-origin",
        });
        if (!response.ok && response.status !== 204) {
            throw new Error(`Failed to delete passkey (HTTP ${response.status}).`);
        }
    }

    /**
     * Lists every passkey registered against the current user.
     * @returns {Promise<Array>}
     */
    async function listPasskeys() {
        const response = await fetch("/auth/passkey/list", {
            method: "GET",
            credentials: "same-origin",
            headers: { "Accept": "application/json" },
        });
        if (!response.ok) {
            throw new Error(`Failed to list passkeys (HTTP ${response.status}).`);
        }
        return response.json();
    }

    window.aisgWebAuthn = {
        registerPasskey,
        loginWithPasskey,
        deletePasskey,
        listPasskeys,
        isSupported: () => !!(window.PublicKeyCredential && navigator.credentials),
    };
})();
