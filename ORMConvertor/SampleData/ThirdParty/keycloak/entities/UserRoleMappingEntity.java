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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/UserRoleMappingEntity.java.
 * Changed: of its 8 named queries, 5 that write are left out and 3 are rewritten, each with a note and, where rewritten, the original text above it; the rest stand as the project wrote them. See README.md.
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
        // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
        //   was: select m from UserRoleMappingEntity m where m.user = :user and m.roleId = :roleId
        @NamedQuery(name = "userHasRole", query = "select m from UserRoleMappingEntity m where m.USER_ID = :userId and m.roleId = :roleId"),
        // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
        //   was: select m from UserRoleMappingEntity m where m.user = :user
        @NamedQuery(name = "userRoleMappings", query = "select m from UserRoleMappingEntity m where m.USER_ID = :userId"),
        // ORMConvertor: rewritten - compares the key column, and takes the key as the parameter, instead of the whole entity.
        //   was: select m.roleId from UserRoleMappingEntity m where m.user = :user
        @NamedQuery(name = "userRoleMappingIds", query = "select m.roleId from UserRoleMappingEntity m where m.USER_ID = :userId")
        // ORMConvertor: "deleteUserRoleMappingsByRealm" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserRoleMappingsByRealmAndLink" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserRoleMappingsByRole" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "deleteUserRoleMappingsByUser" is left out: it is a bulk delete statement, which changes the rows other queries read; the tool translates read-only queries.
        // ORMConvertor: "grantRoleToAllUsers" is left out: it is a bulk insert statement, which changes the rows other queries read; the tool translates read-only queries.
})
@Table(name="USER_ROLE_MAPPING")
@Entity
@IdClass(UserRoleMappingEntity.Key.class)
public class UserRoleMappingEntity  {

    @Id
    @ManyToOne(fetch= FetchType.LAZY)
    @JoinColumn(name="USER_ID")
    protected UserEntity user;

    @Id
    @Column(name = "ROLE_ID")
    protected String roleId;

    public UserEntity getUser() {
        return user;
    }

    public void setUser(UserEntity user) {
        this.user = user;
    }

    public String getRoleId() {
        return roleId;
    }

    public void setRoleId(String roleId) {
        this.roleId = roleId;
    }


    public static class Key implements Serializable {

        protected UserEntity user;

        protected String roleId;

        public Key() {
        }

        public Key(UserEntity user, String roleId) {
            this.user = user;
            this.roleId = roleId;
        }

        public UserEntity getUser() {
            return user;
        }

        public String getRoleId() {
            return roleId;
        }

        @Override
        public boolean equals(Object o) {
            if (this == o) return true;
            if (o == null || getClass() != o.getClass()) return false;

            Key key = (Key) o;

            if (!roleId.equals(key.roleId)) return false;
            if (!user.equals(key.user)) return false;

            return true;
        }

        @Override
        public int hashCode() {
            int result = user.hashCode();
            result = 31 * result + roleId.hashCode();
            return result;
        }
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null) return false;
        if (!(o instanceof UserRoleMappingEntity)) return false;

        UserRoleMappingEntity key = (UserRoleMappingEntity) o;

        if (!roleId.equals(key.roleId)) return false;
        if (!user.equals(key.user)) return false;

        return true;
    }

    @Override
    public int hashCode() {
        int result = user.hashCode();
        result = 31 * result + roleId.hashCode();
        return result;
    }

}
