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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/UserConsentEntity.java.
 * Changed: of its 9 named queries, 6 that write are left out and 3 are rewritten, each with a note and, where rewritten, the original text above it; the rest stand as the project wrote them. See README.md.
 */

package org.keycloak.models.jpa.entities;

import java.util.Collection;
import java.util.LinkedList;

import jakarta.persistence.Access;
import jakarta.persistence.AccessType;
import jakarta.persistence.CascadeType;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.NamedQueries;
import jakarta.persistence.NamedQuery;
import jakarta.persistence.OneToMany;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;

/**
 * @author <a href="mailto:mposolda@redhat.com">Marek Posolda</a>
 */
@Entity
@Table(name="USER_CONSENT", uniqueConstraints = {
        @UniqueConstraint(columnNames = {"USER_ID", "CLIENT_ID"})
})
@NamedQueries({
        // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
        //   was: select consent from UserConsentEntity consent where consent.user.id = :userId and consent.clientId = :clientId
        @NamedQuery(name = "userConsentByUserAndClient", query = "select consent from UserConsentEntity consent where exists ( select u from UserEntity u where consent.user = u and u.id = :userId) and consent.clientId = :clientId"),
        // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
        //   was: select consent from UserConsentEntity consent where consent.user.id = :userId and consent.clientStorageProvider = :clientStorageProvider and consent.externalClientId = :externalClientId
        @NamedQuery(name = "userConsentByUserAndExternalClient", query = "select consent from UserConsentEntity consent where exists ( select u from UserEntity u where consent.user = u and u.id = :userId) and consent.clientStorageProvider = :clientStorageProvider and consent.externalClientId = :externalClientId"),
        // ORMConvertor: rewritten - asks for the user through a correlated exists that compares the association with the user row, instead of a path through the association; the parameter is the user's id.
        //   was: select consent from UserConsentEntity consent where consent.user.id = :userId
        @NamedQuery(name = "userConsentsByUser", query = "select consent from UserConsentEntity consent where exists ( select u from UserEntity u where consent.user = u and u.id = :userId)")
        // ORMConvertor: "deleteUserConsentsByRealm" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserConsentsByRealmAndLink" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserConsentsByUser" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserConsentsByClient" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserConsentsByExternalClient" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserConsentsByClientStorageProvider" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
})
public class UserConsentEntity {

    @Id
    @Column(name="ID", length = 36)
    @Access(AccessType.PROPERTY) // we do this because relationships often fetch id, but not entity.  This avoids an extra SQL
    protected String id;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name="USER_ID")
    protected UserEntity user;

    @Column(name="CLIENT_ID")
    protected String clientId;

    @Column(name="CLIENT_STORAGE_PROVIDER")
    protected String clientStorageProvider;

    @Column(name="EXTERNAL_CLIENT_ID")
    protected String externalClientId;

    @OneToMany(cascade ={CascadeType.REMOVE}, orphanRemoval = true, mappedBy = "userConsent")
    Collection<UserConsentClientScopeEntity> grantedClientScopes = new LinkedList<>();

    @Column(name = "CREATED_DATE")
    private Long createdDate;

    @Column(name = "LAST_UPDATED_DATE")
    private Long lastUpdatedDate;

    public String getId() {
        return id;
    }

    public void setId(String id) {
        this.id = id;
    }

    public UserEntity getUser() {
        return user;
    }

    public void setUser(UserEntity user) {
        this.user = user;
    }

    public Collection<UserConsentClientScopeEntity> getGrantedClientScopes() {
        if (grantedClientScopes == null) {
            grantedClientScopes = new LinkedList<>();
        }
        return grantedClientScopes;
    }

    public void setGrantedClientScopes(Collection<UserConsentClientScopeEntity> grantedClientScopes) {
        this.grantedClientScopes = grantedClientScopes;
    }

    public Long getCreatedDate() {
        return createdDate;
    }

    public void setCreatedDate(Long createdDate) {
        this.createdDate = createdDate;
    }

    public Long getLastUpdatedDate() {
        return lastUpdatedDate;
    }

    public void setLastUpdatedDate(Long lastUpdatedDate) {
        this.lastUpdatedDate = lastUpdatedDate;
    }

    public String getClientId() {
        return clientId;
    }

    public void setClientId(String clientId) {
        this.clientId = clientId;
    }

    public String getClientStorageProvider() {
        return clientStorageProvider;
    }

    public void setClientStorageProvider(String clientStorageProvider) {
        this.clientStorageProvider = clientStorageProvider;
    }

    public String getExternalClientId() {
        return externalClientId;
    }

    public void setExternalClientId(String externalClientId) {
        this.externalClientId = externalClientId;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null) return false;
        if (!(o instanceof UserConsentEntity)) return false;

        UserConsentEntity that = (UserConsentEntity) o;

        if (!id.equals(that.getId())) return false;

        return true;
    }

    @Override
    public int hashCode() {
        return id.hashCode();
    }
}
