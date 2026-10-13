/*
 * Copyright 2017 Red Hat, Inc. and/or its affiliates
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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/UserConsentClientScopeEntity.java.
 * Changed: the key part userConsentId (USER_CONSENT_ID) is stated as its own @Id column beside
 * a read-only @ManyToOne over the same column, and the nested key class holds the column; the
 * original puts @Id on the @ManyToOne itself. See README.md in the folder above.
 */

package org.keycloak.models.jpa.entities;

import java.io.Serializable;
import java.util.Optional;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.NamedQueries;
import jakarta.persistence.NamedQuery;
import jakarta.persistence.Table;

/**
 * @author <a href="mailto:mposolda@redhat.com">Marek Posolda</a>
 */
@NamedQueries({
        @NamedQuery(name="deleteUserConsentClientScopesByRealm", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.user IN (select user from UserEntity user where user.realmId = :realmId))"),
        @NamedQuery(name="deleteUserConsentClientScopesByRealmAndLink", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.user IN (select u from UserEntity u where u.realmId=:realmId and u.federationLink=:link))"),
        @NamedQuery(name="deleteUserConsentClientScopesByUser", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.user = :user)"),
        @NamedQuery(name="deleteUserConsentClientScopesByClientScope", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.scopeId = :scopeId"),
        @NamedQuery(name="deleteUserConsentClientScopesByClient", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.clientId = :clientId)"),
        @NamedQuery(name="deleteUserConsentClientScopesByExternalClient", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.clientStorageProvider = :clientStorageProvider and consent.externalClientId = :externalClientId)"),
        @NamedQuery(name="deleteUserConsentClientScopesByClientStorageProvider", query="delete from UserConsentClientScopeEntity grantedScope where grantedScope.userConsent IN (select consent from UserConsentEntity consent where consent.clientStorageProvider = :clientStorageProvider)"),
})
@Entity
@Table(name="USER_CONSENT_CLIENT_SCOPE")
@IdClass(UserConsentClientScopeEntity.Key.class)
public class UserConsentClientScopeEntity {

    public static String NOT_AVAILABLE_PARAM = "#N A#";

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "USER_CONSENT_ID")
    protected String userConsentId;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "USER_CONSENT_ID", insertable = false, updatable = false)
    protected UserConsentEntity userConsent;

    @Id
    @Column(name="SCOPE_ID")
    protected String scopeId;

    @Id
    @Column(name="PARAMETER")
    protected String parameter;

    public UserConsentEntity getUserConsent() {
        return userConsent;
    }

    public void setUserConsent(UserConsentEntity userConsent) {
        this.userConsent = userConsent;
    }

    public String getScopeId() {
        return scopeId;
    }

    public void setScopeId(String scopeId) {
        this.scopeId = scopeId;
    }

    public Optional<String> getParameter() {
        return Optional.ofNullable(NOT_AVAILABLE_PARAM.equals(parameter)
                ? null
                : parameter);
    }

    public void setParameter(String parameter) {
        this.parameter = parameter == null
                ? NOT_AVAILABLE_PARAM
                : parameter;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null) return false;
        if (!(o instanceof UserConsentClientScopeEntity)) return false;

        UserConsentClientScopeEntity that = (UserConsentClientScopeEntity)o;
        UserConsentClientScopeEntity.Key myKey = new UserConsentClientScopeEntity.Key(this.userConsent, this.scopeId, this.parameter);
        UserConsentClientScopeEntity.Key hisKey = new UserConsentClientScopeEntity.Key(that.userConsent, that.scopeId, that.parameter);
        return myKey.equals(hisKey);
    }

    @Override
    public int hashCode() {
        UserConsentClientScopeEntity.Key myKey = new UserConsentClientScopeEntity.Key(this.userConsent, this.scopeId, this.parameter);
        return myKey.hashCode();
    }

    public static class Key implements Serializable {

        protected String userConsentId;

        protected String scopeId;

        protected String parameter;

        public Key() {
        }

        public Key(String userConsentId, String scopeId, String parameter) {
            this.userConsentId = userConsentId;
            this.scopeId = scopeId;
            this.parameter = parameter;
        }

        public String getUserConsentId() {
            return userConsentId;
        }

        public String getScopeId() {
            return scopeId;
        }

        public String getParameter() {
            return parameter;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            UserConsentClientScopeEntity.Key key = (UserConsentClientScopeEntity.Key) o;

            if (userConsentId != null ? !userConsentId.getId().equals(key.userConsentId != null ? key.userConsentId.getId() : null) : key.userConsentId != null) return false;
            if (scopeId != null ? !scopeId.equals(key.scopeId) : key.scopeId != null) return false;
            if (parameter != null ? !parameter.equals(key.parameter) : key.parameter != null) return false;

            return true;
        }

        @Override
        public int hashCode() {
            int result = userConsentId != null ? userConsentId.getId().hashCode() : 0;
            result = 31 * result + (scopeId != null ? scopeId.hashCode() : 0);
            result = 31 * result + (parameter != null ? parameter.hashCode() : 0);
            return result;
        }
    }
}
