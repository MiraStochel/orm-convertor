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
 * Changed: of its 2 named queries, 2 that write are left out, each with a note above it; the rest stand as the project wrote them. See README.md.
 */

package org.keycloak.models.jpa.entities;

import java.io.Serializable;

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
// ORMConvertor: "deleteClientScopeRoleMappingByRole" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
// ORMConvertor: "deleteClientScopeRoleMappingByClientScope" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
@Table(name="CLIENT_SCOPE_ROLE_MAPPING")
@Entity
@IdClass(ClientScopeRoleMappingEntity.Key.class)
public class ClientScopeRoleMappingEntity {

    @Id
    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "SCOPE_ID")
    protected ClientScopeEntity clientScope;

    @Id
    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name="ROLE_ID")
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

        protected ClientScopeEntity clientScope;

        protected RoleEntity role;

        public Key() {
        }

        public Key(ClientScopeEntity clientScope, RoleEntity role) {
            this.clientScope = clientScope;
            this.role = role;
        }

        public ClientScopeEntity getClientScope() {
            return clientScope;
        }

        public RoleEntity getRole() {
            return role;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            Key key = (Key) o;

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
