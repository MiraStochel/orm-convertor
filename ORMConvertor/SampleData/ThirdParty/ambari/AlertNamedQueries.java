/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * ORMConvertor: this file is not part of Apache Ambari. It carries the read-only named
 * queries of the alert entities beside it, one method each, handed to the EntityManager with
 * em.createQuery, because the tool reads a query where code hands it over and not from
 * @NamedQuery. A query the tool reads only in another form is rewritten, with a note and its
 * original text.
 */
package org.apache.ambari.server.orm.dao;

import jakarta.persistence.EntityManager;
import jakarta.persistence.TypedQuery;

import org.apache.ambari.server.orm.entities.*;

public final class AlertNamedQueries {

    /** Named query "AlertCurrentEntity.findAll". */
    public static TypedQuery<AlertCurrentEntity> currentFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByCluster". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.clusterId = :clusterId
    public static TypedQuery<AlertCurrentEntity> currentFindByCluster(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.clusterId = :clusterId)
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByDefinitionId". */
    // ORMConvertor: rewritten - compares the key column definitionId, which holds the definition's key, instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertDefinition.definitionId = :definitionId
    public static TypedQuery<AlertCurrentEntity> currentFindByDefinitionId(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE alert.definitionId = :definitionId
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByHostAndName". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association, and for its definition the same way over alertDefinitionId.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.clusterId = :clusterId AND alert.alertHistory.alertDefinition.definitionName = :definitionName AND alert.alertHistory.hostName = :hostName
    public static TypedQuery<AlertCurrentEntity> currentFindByHostAndName(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.clusterId = :clusterId
                    AND history.hostName = :hostName
                    AND EXISTS (
                        SELECT definition
                        FROM AlertDefinitionEntity definition
                        WHERE definition.definitionId = history.alertDefinitionId
                        AND definition.definitionName = :definitionName))
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByNameAndNoHost". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association, and for its definition the same way over alertDefinitionId.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.clusterId = :clusterId AND alert.alertHistory.alertDefinition.definitionName = :definitionName AND alert.alertHistory.hostName IS NULL
    public static TypedQuery<AlertCurrentEntity> currentFindByNameAndNoHost(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.clusterId = :clusterId
                    AND history.hostName IS NULL
                    AND EXISTS (
                        SELECT definition
                        FROM AlertDefinitionEntity definition
                        WHERE definition.definitionId = history.alertDefinitionId
                        AND definition.definitionName = :definitionName))
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByHostComponent". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.serviceName = :serviceName AND alert.alertHistory.componentName = :componentName AND alert.alertHistory.hostName = :hostName
    public static TypedQuery<AlertCurrentEntity> currentFindByHostComponent(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.serviceName = :serviceName
                    AND history.componentName = :componentName
                    AND history.hostName = :hostName)
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByHost". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.hostName = :hostName
    public static TypedQuery<AlertCurrentEntity> currentFindByHost(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.hostName = :hostName)
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findByServiceName". */
    // ORMConvertor: rewritten - asks for the alert history through a correlated EXISTS over the key column historyId instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertHistory.serviceName = :serviceName
    public static TypedQuery<AlertCurrentEntity> currentFindByServiceName(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT history
                    FROM AlertHistoryEntity history
                    WHERE history.alertId = alert.historyId
                    AND history.serviceName = :serviceName)
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertCurrentEntity.findDisabled". */
    // ORMConvertor: rewritten - asks for the definition through a correlated EXISTS over the key column definitionId instead of a path through the association.
    //   was: SELECT alert FROM AlertCurrentEntity alert WHERE alert.alertDefinition.enabled = 0
    public static TypedQuery<AlertCurrentEntity> currentFindDisabled(EntityManager em) {
        return em.createQuery("""
                SELECT alert
                FROM AlertCurrentEntity alert
                WHERE EXISTS (
                    SELECT definition
                    FROM AlertDefinitionEntity definition
                    WHERE definition.definitionId = alert.definitionId
                    AND definition.enabled = 0)
                """, AlertCurrentEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findAll". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findAllInCluster". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindAllInCluster(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.clusterId = :clusterId
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findAllEnabledInCluster". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindAllEnabledInCluster(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.clusterId = :clusterId
                AND ad.enabled = 1
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findByName". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindByName(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.definitionName = :definitionName
                AND ad.clusterId = :clusterId
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findByService". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindByService(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.serviceName = :serviceName
                AND ad.clusterId = :clusterId
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findByServiceAndComponent". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindByServiceAndComponent(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.serviceName = :serviceName
                AND ad.componentName = :componentName
                AND ad.clusterId = :clusterId
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertDefinitionEntity.findByIds". */
    public static TypedQuery<AlertDefinitionEntity> definitionFindByIds(EntityManager em) {
        return em.createQuery("""
                SELECT ad
                FROM AlertDefinitionEntity ad
                WHERE ad.definitionId IN :definitionIds
                """, AlertDefinitionEntity.class);
    }

    /** Named query "AlertGroupEntity.findAll". */
    public static TypedQuery<AlertGroupEntity> groupFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT alertGroup
                FROM AlertGroupEntity alertGroup
                """, AlertGroupEntity.class);
    }

    /** Named query "AlertGroupEntity.findAllInCluster". */
    public static TypedQuery<AlertGroupEntity> groupFindAllInCluster(EntityManager em) {
        return em.createQuery("""
                SELECT alertGroup
                FROM AlertGroupEntity alertGroup
                WHERE alertGroup.clusterId = :clusterId
                """, AlertGroupEntity.class);
    }

    /** Named query "AlertGroupEntity.findByNameInCluster". */
    public static TypedQuery<AlertGroupEntity> groupFindByNameInCluster(EntityManager em) {
        return em.createQuery("""
                SELECT alertGroup
                FROM AlertGroupEntity alertGroup
                WHERE alertGroup.groupName = :groupName
                AND alertGroup.clusterId = :clusterId
                """, AlertGroupEntity.class);
    }

    /** Named query "AlertGroupEntity.findServiceDefaultGroup". */
    public static TypedQuery<AlertGroupEntity> groupFindServiceDefaultGroup(EntityManager em) {
        return em.createQuery("""
                SELECT alertGroup
                FROM AlertGroupEntity alertGroup
                WHERE alertGroup.clusterId = :clusterId
                AND alertGroup.serviceName = :serviceName
                AND alertGroup.isDefault = 1
                """, AlertGroupEntity.class);
    }

    /** Named query "AlertGroupEntity.findByIds". */
    public static TypedQuery<AlertGroupEntity> groupFindByIds(EntityManager em) {
        return em.createQuery("""
                SELECT alertGroup
                FROM AlertGroupEntity alertGroup
                WHERE alertGroup.groupId IN :groupIds
                """, AlertGroupEntity.class);
    }

    /** Named query "AlertHistoryEntity.findAll". */
    public static TypedQuery<AlertHistoryEntity> historyFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory
                FROM AlertHistoryEntity alertHistory
                """, AlertHistoryEntity.class);
    }

    /** Named query "AlertHistoryEntity.findAllInCluster". */
    public static TypedQuery<AlertHistoryEntity> historyFindAllInCluster(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.clusterId = :clusterId
                """, AlertHistoryEntity.class);
    }

    /** Named query "AlertHistoryEntity.findAllInClusterBetweenDates". */
    public static TypedQuery<AlertHistoryEntity> historyFindAllInClusterBetweenDates(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.clusterId = :clusterId
                AND alertHistory.alertTimestamp BETWEEN :startDate
                AND :endDate
                """, AlertHistoryEntity.class);
    }

    /** Named query "AlertHistoryEntity.findAllInClusterBeforeDate". */
    public static TypedQuery<AlertHistoryEntity> historyFindAllInClusterBeforeDate(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.clusterId = :clusterId
                AND alertHistory.alertTimestamp <= :beforeDate
                """, AlertHistoryEntity.class);
    }

    /** Named query "AlertHistoryEntity.findAllIdsInClusterBeforeDate". */
    public static TypedQuery<Long> historyFindAllIdsInClusterBeforeDate(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory.alertId
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.clusterId = :clusterId
                AND alertHistory.alertTimestamp <= :beforeDate
                """, Long.class);
    }

    /** Named query "AlertHistoryEntity.findAllInClusterAfterDate". */
    public static TypedQuery<AlertHistoryEntity> historyFindAllInClusterAfterDate(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.clusterId = :clusterId
                AND alertHistory.alertTimestamp >= :afterDate
                """, AlertHistoryEntity.class);
    }

    /** Named query "AlertHistoryEntity.findHistoryIdsByDefinitionId". */
    public static TypedQuery<Long> historyFindHistoryIdsByDefinitionId(EntityManager em) {
        return em.createQuery("""
                SELECT alertHistory.alertId
                FROM AlertHistoryEntity alertHistory
                WHERE alertHistory.alertDefinitionId = :definitionId
                ORDER BY alertHistory.alertId
                """, Long.class);
    }

    /** Named query "AlertNoticeEntity.findAll". */
    public static TypedQuery<AlertNoticeEntity> noticeFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT notice
                FROM AlertNoticeEntity notice
                """, AlertNoticeEntity.class);
    }

    /** Named query "AlertNoticeEntity.findByUuid". */
    public static TypedQuery<AlertNoticeEntity> noticeFindByUuid(EntityManager em) {
        return em.createQuery("""
                SELECT notice
                FROM AlertNoticeEntity notice
                WHERE notice.uuid = :uuid
                """, AlertNoticeEntity.class);
    }

    /** Named query "AlertNoticeEntity.findByHistoryIds". */
    public static TypedQuery<AlertNoticeEntity> noticeFindByHistoryIds(EntityManager em) {
        return em.createQuery("""
                SELECT notice
                FROM AlertNoticeEntity notice
                WHERE notice.historyId IN :historyIds
                """, AlertNoticeEntity.class);
    }

    /** Named query "AlertTargetEntity.findAll". */
    public static TypedQuery<AlertTargetEntity> targetFindAll(EntityManager em) {
        return em.createQuery("""
                SELECT alertTarget
                FROM AlertTargetEntity alertTarget
                """, AlertTargetEntity.class);
    }

    /** Named query "AlertTargetEntity.findAllGlobal". */
    public static TypedQuery<AlertTargetEntity> targetFindAllGlobal(EntityManager em) {
        return em.createQuery("""
                SELECT alertTarget
                FROM AlertTargetEntity alertTarget
                WHERE alertTarget.isGlobal = 1
                """, AlertTargetEntity.class);
    }

    /** Named query "AlertTargetEntity.findByName". */
    public static TypedQuery<AlertTargetEntity> targetFindByName(EntityManager em) {
        return em.createQuery("""
                SELECT alertTarget
                FROM AlertTargetEntity alertTarget
                WHERE alertTarget.targetName = :targetName
                """, AlertTargetEntity.class);
    }

    /** Named query "AlertTargetEntity.findByIds". */
    public static TypedQuery<AlertTargetEntity> targetFindByIds(EntityManager em) {
        return em.createQuery("""
                SELECT alertTarget
                FROM AlertTargetEntity alertTarget
                WHERE alertTarget.targetId IN :targetIds
                """, AlertTargetEntity.class);
    }

    // ORMConvertor: "AlertCurrentEntity.findByService" is left out: it compares a parameter with an attribute of an enum type the conversion does not hold, so the generated method could not type it.
    // ORMConvertor: "AlertDefinitionEntity.findByServiceMaster" is left out: it compares a parameter with an attribute of an enum type the conversion does not hold, so the generated method could not type it; it also names the enum constant by its Java path.
    // ORMConvertor: "AlertDefinitionEntity.findBySourceType" is left out: it compares a parameter with an attribute of an enum type the conversion does not hold, so the generated method could not type it.
    // ORMConvertor: "AlertGroupEntity.findByAssociatedDefinition" is left out: it asks MEMBER OF a many-to-many with the whole entity as the parameter, which the tool reads in no form.
    // ORMConvertor: "AlertHistoryEntity.findAllInClusterWithState" is left out: it compares a parameter with an attribute of an enum type the conversion does not hold, so the generated method could not type it.
    // ORMConvertor: "AlertNoticeEntity.findByState" is left out: it compares a parameter with an attribute of an enum type the conversion does not hold, so the generated method could not type it.
}
