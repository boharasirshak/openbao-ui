# Recovery and break-glass notes

The wrapper does not unseal OpenBao, handle recovery keys or upgrade the server. Keep those procedures separate from this repository and test them against the pinned OpenBao version.

If a control-plane token is suspected to be exposed:

1. Revoke the token in OpenBao using the separate administrative process.
2. Issue a replacement narrowly scoped to mount, identity, policy and AppRole operations.
3. Rotate any affected Userpass passwords, AppRole Secret IDs and database leases.
4. Review the OpenBao audit device for authentication, policy and secret access events.

If the wrapper is unavailable, OpenBao remains the source of truth and can be operated through its approved administrative path.
