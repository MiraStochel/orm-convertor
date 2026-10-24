# Keycloak: users, roles, groups, clients

- **Project:** [keycloak/keycloak](https://github.com/keycloak/keycloak), an identity and access
  management server; JPA entities on Hibernate, `jakarta.persistence` annotations.
- **Commit:** `5cbbdebe021e67ca834ed074dc59e13b3c224cfa` (2026-10-13).
- **Licence:** Apache License 2.0, [`LICENSE.txt`](LICENSE.txt); the project has no NOTICE file.
- **Taken from:** `model/jpa/src/main/java/org/keycloak/models/jpa/entities/`, 22 of the 47
  entity classes there - the user, its attributes, credentials, federated identities, consents and
  required actions; roles with their attributes and composites; groups with their attributes and
  role mappings; clients and client scopes with their attributes, mappings and protocol mappers.

## What was changed

Keycloak keeps its queries in `@NamedQuery` annotations on the entities and runs them with
`em.createNamedQuery`; the tool reads a named query where it is defined, so the queries stay in
the annotations. Every change is marked in the file where it stands, with a note above the
query, and the opening note of each file counts the changes it carries.

1. **The 51 named queries that delete, update or insert are left out**, each with a note in
   place of its annotation: the tool translates read-only queries.
2. **Thirteen of the 48 read-only queries are rewritten**, each with a note and its original
   text above it:
   - a comparison with a whole entity (`m.user = :user`) compares the key column with the key
     (`m.USER_ID = :userId`) - the tool does not read an entity as a parameter;
   - the projection of an association (`select link.user from FederatedIdentityEntity link ...`)
     selects the user and asks for the link through a correlated `exists` - the tool does not
     read a reference as a projection;
   - `count(m.user)` counts the key column `m.USER_ID` - the tool does not read an association
     as the argument of an aggregate.

   The queries with a path through an association (`consent.user.id = :userId`,
   `m.user.realmId = :realmId`, the `c.childRole.id` of
   `select r from RoleEntity r join CompositeRoleEntity c on ...`) and the two that join the
   attribute collection and project the user (`select u from UserEntity u join u.attributes attr ...`)
   stand as the project wrote them: the tool reads a path through an association as the foreign
   key column or as an implicit join, and the projection of one side of a join.
3. **One query is left out:** `getAllRedirectUrisOfEnabledClients` joins the element collection
   `client.redirectUris` and builds a `map`, neither of which the tool reads.
4. **Three element collections of maps are left out** with their accessors, each with a note in
   its place: `authFlowBindings` and `registeredNodes` of `ClientEntity` and `config` of
   `ProtocolMapperEntity` (`@ElementCollection Map<String, …>` over a collection table). A
   collection table is not a column of the entity's table and the tool does not translate it,
   while the property itself went into the generated classes with a Java type (`Map<,>`,
   `Integer`) that no .NET target compiles - the first check of the experiment run found the
   NHibernate and Dapper translations of the excerpt not compilable for it (2026-10-24).
5. **One association to an entity outside the excerpt is rewritten as its column**, with the
   original text above it: `GroupEntity.organization` (`@ManyToOne` over `ORG_ID` to
   `OrganizationEntity`) became `@Column(name = "ORG_ID") String organizationId`. The same
   check found the generated classes naming `OrganizationEntity`, a type nobody declares;
   taking the entity in would have pulled in its domains, identity providers and realm.
   `ProtocolMapperEntity`, the other type the check named, is taken in instead (change 4),
   because it refers to nothing outside the excerpt.

Apart from these notes the entities are the project's text. Where Keycloak puts `@Id` on a `@ManyToOne` (JPA derived
identity, for example `@Id @ManyToOne @JoinColumn(name = "USER_ID") UserEntity user` in
`UserRoleMappingEntity`), the tool reads the join column as a key part named after the column, so
a rewritten query that compares the key names that column (`m.USER_ID = :userId`).
