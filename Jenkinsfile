pipeline{
  agent{label 'lin'}
  options{timeout (time: 1, unit:'HOURS')}
  tools{
    maven 'maven'
    jdk 'java'
  }
  stages{
    stage('Build && SonarQube analysis'){
      steps{
        withSonarQubeEnv ('sonarqube'){
        sh '''
            echo "PATH = ${PATH}"
            echo "M2_HOME = ${M2_HOME}"
            mvn -X clean package sonar:sonar
        '''     
        } 
      }
    }
    stage('Quality Gate'){
      steps{
        timeout(time: 1, unit: 'HOURS') {
                    waitForQualityGate abortPipeline: true
        }
      }
    }
    stage('Tomcat Deploy'){
      steps{
        sh '''
           cp /home/ak/jenkins_home/workspace/a-automation/mvn-repo/f9.war /opt/tomcat/webapps/
           /opt/tomcat/bin/startup.sh
        '''    
      }
    }
    stage('ws cleanup'){
      steps{
        cleanWs()
      }
    }
  }
}
