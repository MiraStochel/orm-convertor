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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/ClientScopeRoleMappingEntity.java.
 * Changed: the key parts clientScopeId (SCOPE_ID) and roleId (ROLE_ID) are stated as their own
 * @Id columns beside a read-only @ManyToOne over the same column, and the nested key class
 * holds the column; the original puts @Id on the @ManyToOne itself. See README.md in the
 * folder above.
 */

package org.keycloak.models.jpa.entities;

import java.io.Serializable;

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
 * @author <a href="mailto:bill@burkecentral.com">Bill Burke</a>
 * @version $Revision: 1 $
 */
@NamedQueries({
        @NamedQuery(name="deleteClientScopeRoleMappingByRole", query="delete from ClientScopeRoleMappingEntity where role = :role"),
        @NamedQuery(name="deleteClientScopeRoleMappingByClientScope", query="delete from ClientScopeRoleMappingEntity where clientScope = :clientScope")
})
@Table(name="CLIENT_SCOPE_ROLE_MAPPING")
@Entity
@IdClass(ClientScopeRoleMappingEntity.Key.class)
public class ClientScopeRoleMappingEntity {

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "SCOPE_ID")
    protected String clientScopeId;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "SCOPE_ID", insertable = false, updatable = false)
    protected ClientScopeEntity clientScope;

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "ROLE_ID")
    protected String roleId;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "ROLE_ID", insertable = false, updatable = false)
    protected RoleEntity role;

    public ClientScopeEntity getClientScope() {
        return clientScope;
    }

    public void setClientScope(ClientScopeEntity clientScope) {
        this.clientScope = clientScope;
    }

    public RoleEntity getRole() {
        return role;
    }

    public void setRole(RoleEntity role) {
        this.role = role;
    }

    public static class Key implements Serializable {

        protected String clientScopeId;

        protected String roleId;

        public Key() {
        }

        public Key(String clientScopeId, String roleId) {
            this.clientScopeId = clientScopeId;
            this.roleId = roleId;
        }

        public String getClientScopeId() {
            return clientScopeId;
        }

        public String getRoleId() {
            return roleId;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            Key key = (Key) o;

            if (clientScopeId != null ? !clientScopeId.getId().equals(key.clientScopeId != null ? key.clientScopeId.getId() : null) : key.clientScopeId != null) return false;
            if (roleId != null ? !roleId.getId().equals(key.roleId != null ? key.roleId.getId() : null) : key.roleId != null) return false;

            return true;
        }

        @Override
        public int hashCode() {
            int result = clientScopeId != null ? clientScopeId.getId().hashCode() : 0;
            result = 31 * result + (roleId != null ? roleId.getId().hashCode() : 0);
            return result;
        }
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        if (!(o instanceof ClientScopeRoleMappingEntity)) return false;

        ClientScopeRoleMappingEntity key = (ClientScopeRoleMappingEntity) o;

        if (clientScope != null ? !clientScope.getId().equals(key.clientScope != null ? key.clientScope.getId() : null) : key.clientScope != null) return false;
        if (role != null ? !role.getId().equals(key.role != null ? key.role.getId() : null) : key.role != null) return false;

        return true;
    }

    @Override
    public int hashCode() {
        int result = clientScope != null ? clientScope.getId().hashCode() : 0;
        result = 31 * result + (role != null ? role.getId().hashCode() : 0);
        return result;
    }


}
