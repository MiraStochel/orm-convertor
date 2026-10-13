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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/ClientScopeAttributeEntity.java.
 * Changed: the key part clientScopeId (SCOPE_ID) is stated as its own @Id column beside a
 * read-only @ManyToOne over the same column, and the nested key class holds the column; the
 * original puts @Id on the @ManyToOne itself. See README.md in the folder above.
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
import jakarta.persistence.Table;

/**
 * @author <a href="mailto:mposolda@redhat.com">Marek Posolda</a>
 */
@Table(name="CLIENT_SCOPE_ATTRIBUTES")
@Entity
@IdClass(ClientScopeAttributeEntity.Key.class)
public class ClientScopeAttributeEntity {

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "SCOPE_ID")
    protected String clientScopeId;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "SCOPE_ID", insertable = false, updatable = false)
    protected ClientScopeEntity clientScope;

    @Id
    @Column(name="NAME")
    protected String name;

    @Column(name = "VALUE", length = 2048)
    protected String value;

    public ClientScopeEntity getClientScope() {
        return clientScope;
    }

    public void setClientScope(ClientScopeEntity clientScope) {
        this.clientScope = clientScope;
    }

    public String getName() {
        return name;
    }

    public void setName(String name) {
        this.name = name;
    }

    public String getValue() {
        return value;
    }

    public void setValue(String value) {
        this.value = value;
    }


    public static class Key implements Serializable {

        protected String clientScopeId;

        protected String name;

        public Key() {
        }

        public Key(String clientScopeId, String name) {
            this.clientScopeId = clientScopeId;
            this.name = name;
        }

        public String getClientScopeId() {
            return clientScopeId;
        }

        public String getName() {
            return name;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            ClientScopeAttributeEntity.Key key = (ClientScopeAttributeEntity.Key) o;

            if (clientScopeId != null ? !clientScopeId.getId().equals(key.clientScopeId != null ? key.clientScopeId.getId() : null) : key.clientScopeId != null) return false;
            if (name != null ? !name.equals(key.name != null ? key.name : null) : key.name != null) return false;

            return true;
        }

        @Override
        public int hashCode() {
            int result = clientScopeId != null ? clientScopeId.getId().hashCode() : 0;
            result = 31 * result + (name != null ? name.hashCode() : 0);
            return result;
        }
    }


    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null || getClass() != o.getClass()) return false;
        if (!(o instanceof ClientScopeAttributeEntity)) return false;

        ClientScopeAttributeEntity key = (ClientScopeAttributeEntity) o;

        if (clientScope != null ? !clientScope.getId().equals(key.clientScope != null ? key.clientScope.getId() : null) : key.clientScope != null) return false;
        if (name != null ? !name.equals(key.name != null ? key.name : null) : key.name != null) return false;

        return true;
    }

    @Override
    public int hashCode() {
        int result = clientScope != null ? clientScope.getId().hashCode() : 0;
        result = 31 * result + (name != null ? name.hashCode() : 0);
        return result;
    }
}
