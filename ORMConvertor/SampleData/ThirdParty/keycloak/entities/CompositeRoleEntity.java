/*
 * Copyright 2026 Red Hat, Inc. and/or its affiliates
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
 * ORMConvertor: taken from keycloak/keycloak at 5cbbdeb, model/jpa/src/main/java/org/keycloak/models/jpa/entities/CompositeRoleEntity.java.
 * Changed: the key parts parentRoleId (COMPOSITE) and childRoleId (CHILD_ROLE) are stated as
 * their own @Id columns beside a read-only @ManyToOne over the same column, and the nested key
 * class holds the column; the original puts @Id on the @ManyToOne itself. See README.md in the
 * folder above.
 */

package org.keycloak.models.jpa.entities;

import java.io.Serializable;
import java.util.Objects;

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
 * Manage compmosite role relations.
 * This used to be a @ManyToMany relation in RoleEntity, and before that there was a native query which lead to stale entities.
 * After those attempts, this is now a separate table that avoids iterating over a lot of parents their entries by applying a simple JPA deletion.
 */
@Entity
@Table(name="COMPOSITE_ROLE")
@NamedQueries({
        @NamedQuery(name="deleteRoleFromComposites", query="delete CompositeRoleEntity c where c.parentRole = :role or c.childRole = :role"),
        @NamedQuery(name="deleteSingleCompositeFromRole", query="delete CompositeRoleEntity c where c.parentRole = :parentRole and c.childRole = :childRole"),
})
@IdClass(CompositeRoleEntity.Key.class)
public class CompositeRoleEntity {
    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "COMPOSITE")
    private String parentRoleId;

    @ManyToOne(fetch=FetchType.LAZY)
    @JoinColumn(name = "COMPOSITE", insertable = false, updatable = false)
    private RoleEntity parentRole;

    // ORMConvertor: the key part is stated as its own column and the relation over that
    // column is read-only; the original puts @Id on the @ManyToOne itself.
    @Id
    @Column(name = "CHILD_ROLE")
    private String childRoleId;

    @ManyToOne(fetch=FetchType.LAZY)
    @JoinColumn(name = "CHILD_ROLE", insertable = false, updatable = false)
    private RoleEntity childRole;

    public CompositeRoleEntity() {
    }

    public CompositeRoleEntity(RoleEntity parentRole, RoleEntity childRole) {
        // Fields must not be null otherwise the automatic dependency detection of Hibernate will not work
        this.parentRole = parentRole;
        this.childRole = childRole;
    }

    public RoleEntity getParentRole() {
        return parentRole;
    }

    public void setParentRole(RoleEntity parentRole) {
        this.parentRole = parentRole;
    }

    public RoleEntity getChildRole() {
        return childRole;
    }

    public void setChildRole(RoleEntity childRole) {
        this.childRole = childRole;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (o == null) return false;
        if (!(o instanceof CompositeRoleEntity that)) return false;

        return parentRole.equals(that.parentRole) && childRole.equals(that.childRole);
    }

    @Override
    public int hashCode() {
        return Objects.hash(childRole, parentRole);
    }

    public static class Key implements Serializable {
        private String childRoleId;
        private String parentRoleId;

        public Key() {
        }

        public Key(String parentRoleId, String childRoleId) {
            this.childRoleId = childRoleId;
            this.parentRoleId = parentRoleId;
        }

        public String getChildRoleId() {
            return childRoleId;
        }

        public void setChildRoleId(String childRoleId) {
            this.childRoleId = childRoleId;
        }

        public String getParentRoleId() {
            return parentRoleId;
        }

        public void setParentRoleId(String parentRoleId) {
            this.parentRoleId = parentRoleId;
        }

        @Override
        public boolean equals(Object o) {
            if (!(o instanceof Key key)) return false;
            return Objects.equals(childRoleId, key.childRoleId) && Objects.equals(parentRoleId, key.parentRoleId);
        }

        @Override
        public int hashCode() {
            return Objects.hash(childRoleId, parentRoleId);
        }
    }
}
