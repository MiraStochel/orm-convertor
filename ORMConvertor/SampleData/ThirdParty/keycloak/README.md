# Keycloak: users, roles, groups, clients

- **Project:** [keycloak/keycloak](https://github.com/keycloak/keycloak), an identity and access
  management server; JPA entities on Hibernate, `jakarta.persistence` annotations.
- **Commit:** `5cbbdebe021e67ca834ed074dc59e13b3c224cfa` (2026-10-13).
- **Licence:** Apache License 2.0, [`LICENSE.txt`](LICENSE.txt); the project has no NOTICE file.
- **Taken from:** `model/jpa/src/main/java/org/keycloak/models/jpa/entities/`, 21 of the 47
  entity classes there - the user, its attributes, credentials, federated identities, consents and
  required actions; roles with their attributes and composites; groups with their attributes and
  role mappings; clients and client scopes with their attributes and mappings.

## What was changed

Keycloak keeps its queries in `@NamedQuery` annotations on the entities and runs them with
`em.createNamedQuery`; the tool reads a named query where it is defined, so the queries stay in
the annotations. Every change is marked in the file where it stands, with a note above the
query, and the opening note of each file counts the changes it carries.

1. **The 51 named queries that delete, update or insert are left out**, each with a note in
   place of its annotation: the tool translates read-only queries.
2. **Eighteen of the 48 read-only queries are rewritten**, each with a note and its original
   text above it:
   - a comparison with a whole entity (`m.user = :user`) compares the key column with the key
     (`m.USER_ID = :userId`) - the tool does not read an entity as a parameter;
   - a path through an association in a condition (`consent.user.id = :userId`, the
     `c.childRole.id` of `select r from RoleEntity r join CompositeRoleEntity c on ...`) becomes
     a correlated `exists` - the tool does not read a path outside `join`;
   - `select distinct r ... join` becomes `exists` without `distinct`, which returns each role
     once as `distinct` did.

   The two queries that join the attribute collection and project the user
   (`select u from UserEntity u join u.attributes attr ...`) stand as the project wrote them: the
   tool reads the projection of one side of a join.
3. **One query is left out:** `getAllRedirectUrisOfEnabledClients` joins the element collection
   `client.redirectUris` and builds a `map`, neither of which the tool reads.

Apart from these notes the entities are the project's text. Where Keycloak puts `@Id` on a `@ManyToOne` (JPA derived
identity, for example `@Id @ManyToOne @JoinColumn(name = "USER_ID") UserEntity user` in
`UserRoleMappingEntity`), the tool reads the join column as a key part named after the column, so
a rewritten query that compares the key names that column (`m.USER_ID = :userId`).
