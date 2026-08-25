# Microsoft Entra application roles

The Phase 1 API treats Microsoft Entra as the production authority for user
assignment. It does not store passwords or grant application roles itself.

`entra-app-roles.json` contains the six stable app-role definitions required by
G-02:

- Learner
- Instructor
- Reviewer
- Author
- Administrator
- Operator

Apply the `appRoles` array to the API app registration, then assign users or
groups to roles through the enterprise application. Require assignment for the
enterprise application before the workforce pilot. The API reads the resulting
`roles` claim and still derives the durable subject from validated `tid` and
`oid` claims.

The development runtime uses explicit synthetic headers and cannot be enabled
outside Development or Testing. Its role simulation does not create an Entra
assignment or satisfy a pilot-enrolment condition.

Official references:

- [Configure group claims and app roles in tokens](https://learn.microsoft.com/security/zero-trust/develop/configure-tokens-group-claims-app-roles)
- [Implement authorization with Microsoft.Identity.Web](https://learn.microsoft.com/entra/msidweb/authentication/authorization)
- [Authentication and authorization in Minimal APIs](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0)
