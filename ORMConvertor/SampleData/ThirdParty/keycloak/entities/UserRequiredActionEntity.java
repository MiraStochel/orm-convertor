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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/UserRequiredActionEntity.java.
 * Changed: the key part userId (USER_ID) is stated as its own @Id column beside a read-only
 * @ManyToOne over the same column, and the nested key class holds the column; the original
 * puts @Id on the @ManyToOne itself. See README.md in the folder above.
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
        @NamedQuery(name="deleteUserRequiredActionsByRealm", query="delete from UserRequiredActionEntity action where action.user IN (select u from UserEntity u where u.realmId=:realmId)"),
        @NamedQuery(name="deleteUserRequiredActionsByRealmAndLink", query="delete from UserRequiredActionEntity action where action.user IN (select u from UserEntity u where u.realmId=:realmId and u.federationLink=:link)")
})
@Entity
@Table(name="USER_REQUIRED_ACTION")
@IdClass(UserRequiredActionEntity.Key.class)
public class UserRequiredActionEntity {

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "USER_ID")
    protected String userId;

    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name = "USER_ID", insertable = false, updatable = false)
    protected UserEntity user;

    @Id
    @Column(name="REQUIRED_ACTION")
    protected String action;

    public String getAction() {
        return action;
    }

    public void setAction(String action) {
        this.action = action;
    }

    public UserEntity getUser() {
        return user;
    }

    public void setUser(UserEntity user) {
        this.user = user;
    }

    public static class Key implements Serializable {

        protected String userId;

        protected String action;

        public Key() {
        }

        public Key(String userId, String action) {
            this.userId = userId;
            this.action = action;
        }

        public String getUserId() {
            return userId;
        }

        public String getAction() {
            return action;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            Key key = (Key) o;

            if (action != key.action) return false;
            if (userId != null ? !userId.getId().equals(key.userId != null ? key.userId.getId() : null) : key.userId != null) return false;

            return true;
        }

        @Override
        public int hashCode() {
            int result = userId != null ? userId.getId().hashCode() : 0;
            result = 31 * result + (action != null ? action.hashCode() : 0);
            return result;
        }
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null) return false;
        if (!(o instanceof UserRequiredActionEntity)) return false;

        UserRequiredActionEntity key = (UserRequiredActionEntity) o;

        if (action != key.action) return false;
        if (user != null ? !user.getId().equals(key.user != null ? key.user.getId() : null) : key.user != null) return false;

        return true;
    }

    @Override
    public int hashCode() {
        int result = user != null ? user.getId().hashCode() : 0;
        result = 31 * result + (action != null ? action.hashCode() : 0);
        return result;
    }


}
