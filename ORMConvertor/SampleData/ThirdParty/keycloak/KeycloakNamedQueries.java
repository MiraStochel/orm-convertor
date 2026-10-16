/*
 * Copyright 2016 Red Hat, Inc. and/or its affiliates
 * and other contributors as indicated by the @author tags.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * ORMConvertor: this file is not part of Keycloak. It carries the read-only named queries of
 * the entities beside it, one method each, handed to the EntityManager with em.createQuery,
 * because the tool reads a query where code hands it over and not from @NamedQuery. A query
 * the tool reads only in another form is rewritten, with a note and its original text; one
 * that it cannot read at all is left out, with a note at the end of the class.
 */
package org.keycloak.models.jpa;

import jakarta.persistence.EntityManager;
import jakarta.persistence.TypedQuery;

import org.keycloak.models.jpa.entities.*;

public final class KeycloakNamedQueries {

    /** Named query "getClientById" of ClientEntity. */
    public static TypedQuery<ClientEntity> getClientById(EntityManager em) {
        return em.createQuery("""
                select client
                from ClientEntity client
                where client.id = :id
                and client.realmId = :realm
                """, ClientEntity.class);
    }

    /** Named query "getAlwaysDisplayInConsoleClients" of ClientEntity. */
    public static TypedQuery<String> getAlwaysDisplayInConsoleClients(EntityManager em) {
        return em.createQuery("""
                select client.id
                from ClientEntity client
                where client.alwaysDisplayInConsole = true
                and client.realmId = :realm
                order by client.clientId
                """, String.class);
    }

    /** Named query "findClientIdByClientId" of ClientEntity. */
    public static TypedQuery<String> findClientIdByClientId(EntityManager em) {
        return em.createQuery("""
                select client.id
                from ClientEntity client
                where client.clientId = :clientId
                and client.realmId = :realm
                """, String.class);
    }

    /** Named query "findClientByClientId" of ClientEntity. */
    public static TypedQuery<ClientEntity> findClientByClientId(EntityManager em) {
        return em.createQuery("""
                select client
                from ClientEntity client
                where client.clientId = :clientId
                and client.realmId = :realm
                """, ClientEntity.class);
    }

    /** Named query "clientScopeClientMappingIdsByClient" of ClientScopeClientMappingEntity. */
    public static TypedQuery<String> clientScopeClientMappingIdsByClient(EntityManager em) {
        return em.createQuery("""
                select m.clientScopeId
                from ClientScopeClientMappingEntity m
                where m.clientId = :clientId
                and m.defaultScope = :defaultScope
                """, String.class);
    }

    /** Named query "getClientScopeIds" of ClientScopeEntity. */
    public static TypedQuery<String> getClientScopeIds(EntityManager em) {
        return em.createQuery("""
                select scope.id
                from ClientScopeEntity scope
                where scope.realmId = :realm
                """, String.class);
    }

    /** Named query "getClientScopesByProtocol" of ClientScopeEntity. */
    public static TypedQuery<ClientScopeEntity> getClientScopesByProtocol(EntityManager em) {
        return em.createQuery("""
                select S
                from ClientScopeEntity S
                where S.realmId = :realm
                and S.protocol = :protocol
                """, ClientScopeEntity.class);
    }

    /** Named query "credentialByUser" of CredentialEntity. */
    // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
    //   was: select cred from CredentialEntity cred where cred.user = :user order by cred.priority
    public static TypedQuery<CredentialEntity> credentialByUser(EntityManager em) {
        return em.createQuery("""
                select cred
                from CredentialEntity cred
                where exists (
                    select u
                    from UserEntity u
                    where cred.user = u
                    and u.id = :userId)
                order by cred.priority
                """, CredentialEntity.class);
    }

    /** Named query "findFederatedIdentityByUser" of FederatedIdentityEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select link from FederatedIdentityEntity link where link.user = :user
    public static TypedQuery<FederatedIdentityEntity> findFederatedIdentityByUser(EntityManager em) {
        return em.createQuery("""
                select link
                from FederatedIdentityEntity link
                where link.localUserId = :userId
                """, FederatedIdentityEntity.class);
    }

    /** Named query "findFederatedIdentityByUserAndProvider" of FederatedIdentityEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select link from FederatedIdentityEntity link where link.user = :user and link.identityProvider = :identityProvider
    public static TypedQuery<FederatedIdentityEntity> findFederatedIdentityByUserAndProvider(EntityManager em) {
        return em.createQuery("""
                select link
                from FederatedIdentityEntity link
                where link.localUserId = :userId
                and link.identityProvider = :identityProvider
                """, FederatedIdentityEntity.class);
    }

    /** Named query "findUserByFederatedIdentityAndRealm" of FederatedIdentityEntity. */
    // ORMConvertor: rewritten - selects the user and asks for the link through a correlated exists, instead of projecting the association of the link.
    //   was: select link.user from FederatedIdentityEntity link where link.realmId = :realmId and link.identityProvider = :identityProvider and link.userId = :userId
    public static TypedQuery<UserEntity> findUserByFederatedIdentityAndRealm(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where exists (
                    select link
                    from FederatedIdentityEntity link
                    where link.localUserId = u.id
                    and link.realmId = :realmId
                    and link.identityProvider = :identityProvider
                    and link.userId = :userId)
                """, UserEntity.class);
    }

    /** Named query "getGroupAttributesByNameAndValue" of GroupAttributeEntity. */
    public static TypedQuery<GroupAttributeEntity> getGroupAttributesByNameAndValue(EntityManager em) {
        return em.createQuery("""
                select attr
                from GroupAttributeEntity attr
                where attr.name = :name
                and attr.value = :value
                """, GroupAttributeEntity.class);
    }

    /** Named query "getGroupIdsByParent" of GroupEntity. */
    public static TypedQuery<String> getGroupIdsByParent(EntityManager em) {
        return em.createQuery("""
                select u.id
                from GroupEntity u
                where u.realm = :realm
                and u.type = 0
                and u.parentId = :parent
                order by u.name ASC
                """, String.class);
    }

    /** Named query "getChildGroupIdsByParentIds" of GroupEntity. */
    public static TypedQuery<String> getChildGroupIdsByParentIds(EntityManager em) {
        return em.createQuery("""
                select g.id
                from GroupEntity g
                where g.realm = :realm
                and g.type = 0
                and g.parentId in :parentIds
                """, String.class);
    }

    /** Named query "groupHasRole" of GroupRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from GroupRoleMappingEntity m where m.group = :group and m.roleId = :roleId
    public static TypedQuery<GroupRoleMappingEntity> groupHasRole(EntityManager em) {
        return em.createQuery("""
                select m
                from GroupRoleMappingEntity m
                where m.groupId = :groupId
                and m.roleId = :roleId
                """, GroupRoleMappingEntity.class);
    }

    /** Named query "groupRoleMappings" of GroupRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from GroupRoleMappingEntity m where m.group = :group
    public static TypedQuery<GroupRoleMappingEntity> groupRoleMappings(EntityManager em) {
        return em.createQuery("""
                select m
                from GroupRoleMappingEntity m
                where m.groupId = :groupId
                """, GroupRoleMappingEntity.class);
    }

    /** Named query "groupRoleMappingIds" of GroupRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m.roleId from GroupRoleMappingEntity m where m.group = :group
    public static TypedQuery<String> groupRoleMappingIds(EntityManager em) {
        return em.createQuery("""
                select m.roleId
                from GroupRoleMappingEntity m
                where m.groupId = :groupId
                """, String.class);
    }

    /** Named query "getClientRoles" of RoleEntity. */
    public static TypedQuery<RoleEntity> getClientRoles(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.clientId = :client
                order by role.name
                """, RoleEntity.class);
    }

    /** Named query "getClientRoleIds" of RoleEntity. */
    public static TypedQuery<String> getClientRoleIds(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.clientId = :client
                """, String.class);
    }

    /** Named query "getClientRoleByName" of RoleEntity. */
    public static TypedQuery<RoleEntity> getClientRoleByName(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.name = :name
                and role.clientId = :client
                """, RoleEntity.class);
    }

    /** Named query "getClientRoleIdByName" of RoleEntity. */
    public static TypedQuery<String> getClientRoleIdByName(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.name = :name
                and role.clientId = :client
                """, String.class);
    }

    /** Named query "searchForClientRoles" of RoleEntity. */
    public static TypedQuery<RoleEntity> searchForClientRoles(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.clientId = :client
                and ( lower(role.name) like :search
                    or lower(role.description) like :search )
                order by role.name
                """, RoleEntity.class);
    }

    /** Named query "getRealmRoles" of RoleEntity. */
    public static TypedQuery<RoleEntity> getRealmRoles(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.clientRole = false
                and role.realmId = :realm
                order by role.name
                """, RoleEntity.class);
    }

    /** Named query "getRealmRoleIds" of RoleEntity. */
    public static TypedQuery<String> getRealmRoleIds(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.clientRole = false
                and role.realmId = :realm
                """, String.class);
    }

    /** Named query "getRealmRoleByName" of RoleEntity. */
    public static TypedQuery<RoleEntity> getRealmRoleByName(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.clientRole = false
                and role.name = :name
                and role.realmId = :realm
                """, RoleEntity.class);
    }

    /** Named query "getRealmRoleIdByName" of RoleEntity. */
    public static TypedQuery<String> getRealmRoleIdByName(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.clientRole = false
                and role.name = :name
                and role.realmId = :realm
                """, String.class);
    }

    /** Named query "searchForRealmRoles" of RoleEntity. */
    public static TypedQuery<RoleEntity> searchForRealmRoles(EntityManager em) {
        return em.createQuery("""
                select role
                from RoleEntity role
                where role.clientRole = false
                and role.realmId = :realm
                and ( lower(role.name) like :search
                    or lower(role.description) like :search )
                order by role.name
                """, RoleEntity.class);
    }

    /** Named query "getRoleIdsFromIdList" of RoleEntity. */
    public static TypedQuery<String> getRoleIdsFromIdList(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.realmId = :realm
                and role.id in :ids
                order by role.name ASC
                """, String.class);
    }

    /** Named query "getRoleIdsByNameContainingFromIdList" of RoleEntity. */
    public static TypedQuery<String> getRoleIdsByNameContainingFromIdList(EntityManager em) {
        return em.createQuery("""
                select role.id
                from RoleEntity role
                where role.realmId = :realm
                and lower(role.name) like lower(concat('%',:search,'%'))
                and role.id in :ids
                order by role.name ASC
                """, String.class);
    }

    /** Named query "getChildRoles" of RoleEntity. */
    // ORMConvertor: rewritten - asks for the child role through a correlated exists over the key columns of the composite role, instead of a join whose condition reaches the key through the association (c.childRole.id).
    //   was: select r from RoleEntity r join CompositeRoleEntity c on r.id = c.childRole.id where c.parentRole.id = :parentRoleId
    public static TypedQuery<RoleEntity> getChildRoles(EntityManager em) {
        return em.createQuery("""
                select r
                from RoleEntity r
                where exists (
                    select c
                    from CompositeRoleEntity c
                    where c.childRoleId = r.id
                    and c.parentRoleId = :parentRoleId)
                """, RoleEntity.class);
    }

    /** Named query "getChildRolesFromParentIds" of RoleEntity. */
    // ORMConvertor: rewritten - asks for the child role through a correlated exists over the key columns of the composite role, instead of a join whose condition reaches the key through the association (c.childRole.id); exists yields each role once, as distinct did.
    //   was: select distinct r from RoleEntity r join CompositeRoleEntity c on r.id = c.childRole.id where c.parentRole.id in :parentRoleIds
    public static TypedQuery<RoleEntity> getChildRolesFromParentIds(EntityManager em) {
        return em.createQuery("""
                select r
                from RoleEntity r
                where exists (
                    select c
                    from CompositeRoleEntity c
                    where c.childRoleId = r.id
                    and c.parentRoleId in :parentRoleIds)
                """, RoleEntity.class);
    }

    /** Named query "userConsentByUserAndClient" of UserConsentEntity. */
    // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
    //   was: select consent from UserConsentEntity consent where consent.user.id = :userId and consent.clientId = :clientId
    public static TypedQuery<UserConsentEntity> userConsentByUserAndClient(EntityManager em) {
        return em.createQuery("""
                select consent
                from UserConsentEntity consent
                where exists (
                    select u
                    from UserEntity u
                    where consent.user = u
                    and u.id = :userId)
                and consent.clientId = :clientId
                """, UserConsentEntity.class);
    }

    /** Named query "userConsentByUserAndExternalClient" of UserConsentEntity. */
    // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
    //   was: select consent from UserConsentEntity consent where consent.user.id = :userId and consent.clientStorageProvider = :clientStorageProvider and consent.externalClientId = :externalClientId
    public static TypedQuery<UserConsentEntity> userConsentByUserAndExternalClient(EntityManager em) {
        return em.createQuery("""
                select consent
                from UserConsentEntity consent
                where exists (
                    select u
                    from UserEntity u
                    where consent.user = u
                    and u.id = :userId)
                and consent.clientStorageProvider = :clientStorageProvider
                and consent.externalClientId = :externalClientId
                """, UserConsentEntity.class);
    }

    /** Named query "userConsentsByUser" of UserConsentEntity. */
    // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
    //   was: select consent from UserConsentEntity consent where consent.user.id = :userId
    public static TypedQuery<UserConsentEntity> userConsentsByUser(EntityManager em) {
        return em.createQuery("""
                select consent
                from UserConsentEntity consent
                where exists (
                    select u
                    from UserEntity u
                    where consent.user = u
                    and u.id = :userId)
                """, UserConsentEntity.class);
    }

    /** Named query "getRealmUserByUsername" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUserByUsername(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where u.username = :username
                and u.realmId = :realmId
                """, UserEntity.class);
    }

    /** Named query "getRealmUserByEmail" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUserByEmail(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where u.email = :email
                and u.realmId = :realmId
                """, UserEntity.class);
    }

    /** Named query "getRealmUserByLastName" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUserByLastName(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where u.lastName = :lastName
                and u.realmId = :realmId
                """, UserEntity.class);
    }

    /** Named query "getRealmUserByFirstLastName" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUserByFirstLastName(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where u.firstName = :first
                and u.lastName = :last
                and u.realmId = :realmId
                """, UserEntity.class);
    }

    /** Named query "getRealmUserByServiceAccount" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUserByServiceAccount(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                where u.serviceAccountClientLink = :clientInternalId
                and u.realmId = :realmId
                """, UserEntity.class);
    }

    /** Named query "getRealmUsersByAttributeNameAndValue" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUsersByAttributeNameAndValue(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                join u.attributes attr
                where u.realmId = :realmId
                and attr.name = :name
                and attr.value = :value
                """, UserEntity.class);
    }

    /** Named query "getRealmUsersByAttributeNameAndLongValue" of UserEntity. */
    public static TypedQuery<UserEntity> getRealmUsersByAttributeNameAndLongValue(EntityManager em) {
        return em.createQuery("""
                select u
                from UserEntity u
                join u.attributes attr
                where u.realmId = :realmId
                and attr.name = :name
                and attr.longValueHash = :longValueHash
                """, UserEntity.class);
    }

    /** Named query "userMemberOf" of UserGroupMembershipEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from UserGroupMembershipEntity m where m.user = :user and m.groupId = :groupId
    public static TypedQuery<UserGroupMembershipEntity> userMemberOf(EntityManager em) {
        return em.createQuery("""
                select m
                from UserGroupMembershipEntity m
                where m.userId = :userId
                and m.groupId = :groupId
                """, UserGroupMembershipEntity.class);
    }

    /** Named query "userGroupMembership" of UserGroupMembershipEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from UserGroupMembershipEntity m where m.user = :user
    public static TypedQuery<UserGroupMembershipEntity> userGroupMembership(EntityManager em) {
        return em.createQuery("""
                select m
                from UserGroupMembershipEntity m
                where m.userId = :userId
                """, UserGroupMembershipEntity.class);
    }

    /** Named query "userCountInGroups" of UserGroupMembershipEntity. */
    // ORMConvertor: rewritten - asks for the realm of the user through a correlated exists over the key column userId instead of a path through the association; counts the key column.
    //   was: select count(m.user) from UserGroupMembershipEntity m where m.user.realmId = :realmId and m.groupId in :groupIds
    public static TypedQuery<Long> userCountInGroups(EntityManager em) {
        return em.createQuery("""
                select count(m.userId)
                from UserGroupMembershipEntity m
                where exists (
                    select u
                    from UserEntity u
                    where u.id = m.userId
                    and u.realmId = :realmId)
                and m.groupId in :groupIds
                """, Long.class);
    }

    /** Named query "userHasRole" of UserRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from UserRoleMappingEntity m where m.user = :user and m.roleId = :roleId
    public static TypedQuery<UserRoleMappingEntity> userHasRole(EntityManager em) {
        return em.createQuery("""
                select m
                from UserRoleMappingEntity m
                where m.userId = :userId
                and m.roleId = :roleId
                """, UserRoleMappingEntity.class);
    }

    /** Named query "userRoleMappings" of UserRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m from UserRoleMappingEntity m where m.user = :user
    public static TypedQuery<UserRoleMappingEntity> userRoleMappings(EntityManager em) {
        return em.createQuery("""
                select m
                from UserRoleMappingEntity m
                where m.userId = :userId
                """, UserRoleMappingEntity.class);
    }

    /** Named query "userRoleMappingIds" of UserRoleMappingEntity. */
    // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
    //   was: select m.roleId from UserRoleMappingEntity m where m.user = :user
    public static TypedQuery<String> userRoleMappingIds(EntityManager em) {
        return em.createQuery("""
                select m.roleId
                from UserRoleMappingEntity m
                where m.userId = :userId
                """, String.class);
    }

    // ORMConvertor: "getAllRedirectUrisOfEnabledClients" of ClientEntity is left out: it joins the element collection client.redirectUris and builds a map, neither of which the tool reads.
}
